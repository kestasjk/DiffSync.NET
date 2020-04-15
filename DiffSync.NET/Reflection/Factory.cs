using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Ink;

using MessagePack;

namespace DiffSync.NET.Reflection
{

    /// <summary>
    /// A factory for constructing syncers for a given class T. All fields in T that are marked as [DiffSync] will be synced, and if [DataContract] and [DataMember] are used on the class and fields 
    /// the syncer will also generate serializable JSON messages, and be able to save its state to disk.
    /// 
    /// All objects being synced are assumed to have Guids to identify them, and if all Syncers that get created are added to a Dictionary of Guid, Syncer the factory has a function to work
    /// through the list, perform all diffs, generate all messages, send them via the given message send function, then returns all Guids which have been updated
    /// 
    /// It also has some custom Diff and Patch logic capabilities, a
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class Factory<T> where T : class, IReflectionSyncable<T>, new()
    {
        public static Syncer Create(Guid objectGuid, T rootLiveItem, int startRevision, T initialShadowItem = null) => new Syncer(objectGuid, rootLiveItem, initialShadowItem ?? new T(), startRevision);
        
        public static Dictionary<Guid, Syncer> LoadDictionary(System.IO.DirectoryInfo cacheFolder) => LoadDictionary(cacheFolder.GetFiles("*.json").ToDictionary(f => Guid.Parse(System.IO.Path.GetFileNameWithoutExtension(f.Name)), d => System.IO.File.ReadAllText(d.FullName)));
        public static Dictionary<Guid, Syncer> LoadDictionary(Dictionary<Guid, string> dict) => dict.ToDictionary(d => d.Key, d => Syncer.Deserialize(d.Value));

        // Loading the server cache involves taking in two Guids, one for the item and one for the session, as an item can have many sessions going.
        public static Dictionary<Guid, Dictionary<Guid, Syncer>> LoadServerDictionary(System.IO.DirectoryInfo cacheFolder)
        {
            var res = new Dictionary<Guid, Dictionary<Guid, string>>();
            foreach(var kvp in cacheFolder.GetFiles("*.json").Select(f =>
            {
                var guids = System.IO.Path.GetFileNameWithoutExtension(f.Name);
                var objectGuid = Guid.Parse(guids.Substring(0, guids.Length / 2));
                var sessionGuid = Guid.Parse(guids.Substring(guids.Length / 2, guids.Length / 2));
                return (objectGuid, sessionGuid, System.IO.File.ReadAllText(f.FullName));
            }).ToList())
            {
                if (!res.ContainsKey(kvp.objectGuid)) res.Add(kvp.objectGuid, new Dictionary<Guid, string>());
                if (!res[kvp.objectGuid].ContainsKey(kvp.sessionGuid)) res[kvp.objectGuid].Add(kvp.sessionGuid, kvp.Item3);
            }
            return LoadServerDictionary(res);
        }
        public static Dictionary<Guid, Dictionary<Guid, Syncer>> LoadServerDictionary(Dictionary<Guid, Dictionary<Guid, string>> dict) => dict.ToDictionary(d => d.Key, d => d.Value.ToDictionary(e => e.Key, e => Syncer.Deserialize(e.Value)));

        /// <summary>
        /// Important function, where all the syncing actually happens; this takes a dictionary of syncers, a send packet delegate and a save to disk delegate. 
        /// Syncers are linked to live objects which may have been updated, with a shadow diff based on the latest server change, these changes will be detected
        /// and diffed against the last version from the server so that the client and server can both apply differences to a common state.
        /// 
        /// Meanwhile the primary read-only channel to the server (downloads of journaled list / item / itemlistlink change records) also receives any updates 
        /// made, and is allocated to its Syncer so that the syncer can detect if it is fully synchronized with the server via DiffSync and the entry itself, which
        /// means the Syncer has done its job and can be closed down.
        /// 
        /// </summary>
        /// <param name="syncers">Dictionary of Syncer objects</param>
        /// <param name="sendPacket">A function to asynchronously send a message back to the server</param>
        /// <param name="saveToDisk">A function that will send a Guid and serialized string to be saved, to then be loaded later</param>
        /// <returns></returns>
        public static async Task<(List<Guid> Sent, List<Guid> Received, List<Guid> Failed, List<Guid> Completed)> SyncDictionary(Dictionary<Guid, Syncer> syncers, Func<MessagePacket, Task<MessagePacket>> sendPacket, Func<Syncer, string, Task> saveToDisk=null, Action<Syncer> onResetSyncerToJournal = null, Action<Syncer, MessagePacket> modifyOutgoingMessage = null)
        {
            var updated = new List<Guid>();
            var sent = new List<Guid>();
            var errored = new List<Guid>();
            var completed = new List<Guid>();

            var completedTasks = new Dictionary<Guid, Task<MessagePacket>>();
            var completionMessageTasks = new Dictionary<Guid, Task<MessagePacket>>();
            {

                Exception exception = null;
                var tasks = new Dictionary<Guid, Task<MessagePacket>>();
                foreach (var i in syncers.ToList())
                {
                    if (i.Value.LatestClientObject != null) {
                        i.Value.LiveObject.CopyStateFrom(i.Value.LatestClientObject);
                    }
                    var hasCheckDifference = false;
                    if (i.Value.ServerCheckCopy != null && !i.Value.HasUnconfirmedEdits)
                    {
                        // A new server check copy to check against.

                        var patcher = new Patcher("Checker -1", i.Value.ServerCheckCopy) { ImportantOnly = true };
                        var serverCheckDiff = patcher.GetDiff(0, i.Value.LiveObject);

                        if (serverCheckDiff != null)
                        {
                            if(serverCheckDiff.DiffFields.Count > 0)
                            {
                                Console.WriteLine("Check against journal record (other = local live)");
                                patcher.PrintDifferences(serverCheckDiff, i.Value.LiveObject);
                                hasCheckDifference = true;
                            }
                        }

                        // No differences with the server; we are synced up! (Don't act yet as MessageCycle() may still emit a message)
                        i.Value.IsSynced = ((serverCheckDiff?.DiffFields?.Count ?? 0) == 0);
                        //i.Value.ServerCheckCopy = null;
                    }

                    var msg = i.Value.ClientMessageCycle(); // This will generate a message if something has changed or is still being synced, handles time outs and repeats etc

                    // Give the caller a chance to modify the message before we decide to send it out. This may involve removing obviously redundant fields which will just extend the
                    // sync process needlessly (e.g. the timestamp field LastLocalUpdate which is important for resolving conflicts, but also very easily causes conflicts between itself)
                    modifyOutgoingMessage?.Invoke(i.Value, msg.ReturnMessage);

                    var sendMessage = false;
                    if (i.Value.IsSynced && !i.Value.HasUnconfirmedEdits && (msg.ReturnMessage?.Message == null || msg.ReturnMessage.Message.Diffs.Count == 0))// && (DateTime.Now - i.Value.LastDiffTime).TotalMinutes > 15) // Don't bother waiting; if we're synced we're synced
                    {
                        // We're synced, there's nothing to send, it has been quiet for a while
                        completed.Add(i.Key);

                        // Don't bother letting the server know we are synced; instead just let it time out and get garbage collected
                        //completionMessageTasks.Add(i.Key, sendPacket(new MessagePacket(i.Value.SessionGuid, i.Value.ObjectGuid, null) { ClientCompleted = true })); // No revision needed to complete
                    }
                    else if (msg.ReturnMessage?.Message != null && msg.ReturnMessage.Message.Diffs.Count == 0 && i.Value.ServerCheckCopy != null && i.Value.IsSynced == false && !msg.PeerVersionChanged && !i.Value.HasUnconfirmedEdits)
                    {
                        // We are not synced with the server, yet we have nothing to send back to the server. This indicates that the diff sync believes we are in sync and just need to 
                        // let the server know we are in sync, when in reality we are working off different shadows.
                        // To resolve this we need to throw out the shadow and start from a new base. The ServerCheckCopy is the obvious way to go.


                        // Just for debugging..
                        var patcher = new Patcher("Checker 0", i.Value.ServerCheckCopy) { ImportantOnly = true };
                        var serverCheckDiff = patcher.GetDiff(0, i.Value.LiveObject);

                        if (serverCheckDiff != null)
                        {
                            Console.WriteLine("Check against journal record (other = local live), nothing to send:");
                            patcher.PrintDifferences(serverCheckDiff, i.Value.LiveObject);
                        }

                        // Set as errored so a new sync session will start
                        errored.Add(i.Value.ObjectGuid);
                    }
                    else if (msg.ReturnMessage != null && msg.ReturnMessage.Message.Diffs.Count > 0)
                    {
                        sendMessage = true;
                    }
                    else if (msg.ReturnMessage != null && (DateTime.Now - i.Value.LastMessageSendTime).TotalSeconds > 30)
                    {
                        // This is a weak reason to continue sending; just due to a "timeout", no differences to sent back
                        // If the server is still sending differences even though we have none to send back after many repeats
                        // the local shadow may have gone wrong somehow. If the shadow goes wrong there is no recovery 
                        // except ditching the shadow and starting from a fresh known shadow (the server's journal copy)
                        if (hasCheckDifference )
                        {
                            if (i.Value.ServerCheckCopy != null)
                            {
                                errored.Add(i.Value.ObjectGuid);
                            }
                            else
                                throw new Exception("Receiving message returns but no check copy");
                        }
                        else
                        {
                            if (i.Value.ServerCheckCopy == null)
                            {
                                // Try and wake this up and get a new copy from the server so this set of changes can be rescued
                                //i.Value.LiveObject.LastUpdated = DateTime.Now;
                                sendMessage = true;
                            }
                            // No check copy, we're not sending any changes.. we're done
                            //completed.Add(i.Key);
                        }
                    }

                    if (sendMessage)
                    {
                        i.Value.LastMessageSendTime = DateTime.Now;

                        if ((msg.ReturnMessage.Message?.Diffs?.Count ?? 0) != 0) i.Value.LastDiffTime = DateTime.Now;

                        tasks.Add(i.Key, sendPacket(msg.ReturnMessage));
                    }
                    if (tasks.Count > 100)
                    {
                        try
                        {
                            await Task.WhenAll(tasks.Values.ToArray());
                            foreach (var t in tasks.ToList())
                            {
                                completedTasks.Add(t.Key, t.Value);
                            }
                            tasks.Clear();
                        }
                        catch (Exception ex)
                        {
                            exception = ex;
                        }
                    }
                }

                if (tasks.Count > 0)
                {
                    try
                    {
                        await Task.WhenAll(tasks.Values.ToArray());
                        foreach (var t in tasks.ToList())
                        {
                            completedTasks.Add(t.Key, t.Value);
                        }
                        tasks.Clear();
                    }
                    catch (Exception ex)
                    {
                        exception = ex;
                    }
                }
            }

            if( completedTasks.Count > 0)
            {

                foreach (var t in completedTasks)
                {
                    if (!t.Value.IsFaulted)
                    {
                        var msg = t.Value.Result;

                        var syncer = syncers[t.Key];

                        if ( msg == null )
                        {
                            errored.Add(t.Key);
                            // This may mean the syncer is bugged; if we sent a message we should get a return message 
                            continue;
                        }
                        else if (msg != null && (msg.Message == null || msg.ServerError != null || msg.ClientCompleted))
                        {
                            errored.Add(t.Key);
                            // This may mean the syncer is bugged; if we sent a message we should get a return message 
                            continue;
                        }

                        syncer.LastMessageRecvTime = DateTime.Now;

                        if ( msg.Message.Diffs.Count != 0 ) syncer.LastDiffTime = DateTime.Now;
                        
                        
                        var msgResponse = syncer.ClientMessageCycle(alwaysGenerateMessage: false, msgReceived: msg);
                        if ( msgResponse.PeerVersionChanged )
                            updated.Add(t.Key);
                        else
                            sent.Add(t.Key);

                        // For debugging.. but may be useful to sync in a single cycle
                        if (syncer.ServerCheckCopy != null)
                        {
                            // A new server check copy to check against.

                            var patcher = new Patcher("Checker 1", syncer.ServerCheckCopy) { ImportantOnly = true };
                            var serverCheckDiff = patcher.GetDiff(0, syncer.LiveObject);

                            if (serverCheckDiff != null)
                            {
                                Console.WriteLine("Check against journal record (other = local live), after main cycle:");
                                patcher.PrintDifferences(serverCheckDiff, syncer.LiveObject);
                            }

                            // No differences with the server; we are synced up! (Don't act yet as MessageCycle() may still emit a message)
                            //i.Value.IsSynced = ((serverCheckDiff?.DiffFields?.Count ?? 0) == 0);
                            //i.Value.ServerCheckCopy = null;
                        }
                    }
                    else
                    {
                        errored.Add(t.Key);
                    }
                }
            }

            if (saveToDisk != null)
            {
                // Save everything that has changed to disk:
                {
                    var saveList = sent.Union(updated).Union(errored).Distinct().ToList();
                    while (saveList.Count > 0)
                    {
                        var saveTasks = new List<Task>();

                        for (int i = 0; i < 100 && i < saveList.Count; i++)
                            saveTasks.Add(saveToDisk(syncers[saveList[i]], syncers[saveList[i]].Serialize()));

                        if (saveList.Count > 0)
                            saveList = saveList.Skip(Math.Min(100, saveList.Count)).ToList();

                        await Task.WhenAll(saveTasks);
                    }
                }

                // And remove anything that has completed:
                {
                    var removeList = completed.ToList();
                    while (removeList.Count > 0)
                    {
                        var removeTasks = new List<Task>();

                        for (int i = 0; i < 100 && i < removeList.Count; i++)
                            removeTasks.Add(saveToDisk(syncers[removeList[i]], null)); // null will trigger the deletion

                        if (removeList.Count > 0)
                            removeList = removeList.Skip(Math.Min(100, removeList.Count)).ToList();

                        await Task.WhenAll(removeTasks);
                    }
                }
            }
            await Task.WhenAll(completionMessageTasks.Values.ToArray());

            return (sent, updated, errored, completed);
        }
        /// <summary>
        /// This class does the nitty gritty detailed work of creating and applying diffs in a way that will only trigger a conflict when necessary, and allow the client / server to take priority as needed,
        /// and handle non-serializable / mergable types.
        /// </summary>
        [DataContract]
        public class Patcher : DiffSync.NET.IDiffSyncable<T, Diff>
        {
            /// <summary>
            /// The state that this patcher is applying diffs to / generating diffs from. If this is the live
            /// patcher this may be the live reference.
            /// </summary>
            [DataMember]
            internal T State { get; private set; }
            public Patcher() { } // For seralization reasons an empty constructor is needed, not for normal use
            /// <summary>
            /// A list of unconfirmed edits so we can reject any changes that are older than the ones we have sent:
            /// </summary>
            internal DiffQueue<Diff> UnconfirmedEdits = new DiffQueue<Diff>();
            private Dictionary<string, DateTime> LatestUnconfirmedEditsByField => UnconfirmedEdits.Diffs.SelectMany(di => di.DiffFields).GroupBy(g => g.Key).ToDictionary(g => g.Key, g => g.Select(h => h.Value).Max());
            public void PrintDifferences(Diff d, T o)
            {

                if (Properties == null) GenerateReflectionData();

                var aData = GetStateData();

                var bData = o;
                Console.WriteLine("Local: "+ Name + " vs Diff");

                var unconfirmedDict = LatestUnconfirmedEditsByField;
                foreach (var name in d.DiffFields)
                {
                    Console.WriteLine("DiffField: " + name.Key);
                    if (unconfirmedDict.ContainsKey(name.Key) )
                        Console.WriteLine("Local Modified: " + unconfirmedDict[name.Key].ToString("s"));
                    else
                        Console.WriteLine("Local Modified: N/A");
                    Console.WriteLine("Diff Modified: " + name.Value.ToString("s"));
                    foreach (var p in Properties.Where(p => p.Name == name.Key))
                    {
                        Console.WriteLine("Local: " + p.GetValue(aData));
                        Console.WriteLine("Diff: " + p.GetValue(bData));
                    }
                    foreach (var f in Fields.Where(f => f.Name == name.Key))
                    {
                        Console.WriteLine("Local: " + f.GetValue(aData));
                        Console.WriteLine("Diff: " + f.GetValue(bData));
                    }
                }
                Console.WriteLine("------");
            }
            /// <summary>
            /// This is set by the server so that fields updated from other sessions can be used to block older changes from newer clients
            /// </summary>
            [DataMember]
            public Dictionary<string, DateTime> ServerFieldsUpdated = null;
            public void SetServerTimestamps(Dictionary<string, DateTime> serverFieldsUpdated)
            {
                ServerFieldsUpdated = serverFieldsUpdated;
                // This is the server updating 
            }
            [DataMember]
            public string Name ;
            public Patcher(string name, T state)
            {
                Name = name;
                State = new T();
                State.CopyStateFrom(state);
            }
            private static object initializeLock = new object();
            // This should only contain values for a specific type T; if this has fieldinfos for multiple types there is a problem
            private static List<FieldInfo> Fields = null;
            private static List<PropertyInfo> Properties = null;

            private static void GenerateReflectionData()
            {
                lock (initializeLock)
                {
                    if (Properties == null)
                    {
                        Properties = typeof(T).GetProperties(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).Where(prop => Attribute.IsDefined(prop, typeof(DiffSyncAttribute))).ToList();
                        Fields = typeof(T).GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).Where(prop => Attribute.IsDefined(prop, typeof(DiffSyncAttribute))).ToList();
                    }
                }

            }
            private static Dictionary<Point, Stroke> ByteToStrokes(byte[] mem)
            {
                if (mem == null || mem.Length == 0) return null;
                using (var ms = new MemoryStream(mem))
                {
                    // Remove any strokes that start from the exact same spot
                    return new StrokeCollection(ms).Where(s => s.StylusPoints.Count > 0).GroupBy(s => s.StylusPoints[0].ToPoint()).ToDictionary(p => p.Key, p => p.First());
                }
            }
            private static byte[] StrokeToBytes(StrokeCollection strokes)
            {

                using (var ms = new MemoryStream())
                {
                    // Remove any strokes that start from the exact same spot
                    strokes.Save(ms);
                    ms.Position = 0;
                    return ms.ToArray();
                }
            }

            /// <summary>
            /// This returns a copy, and is thread-safe (or at least should be ..)
            /// </summary>
            /// <returns></returns>
            public T GetStateData() => new T().CopyStateFrom(State);

            // This is only used to accept a new updated object
            internal void SetStateData(T state)
            {
                state = new T().CopyStateFrom(state); // Ensure this object isn't linked to anything outside

                State.CopyStateFrom(state);
            }

                        
        public void Apply(Diff data, bool? isResponse, bool isShadow)
            {

                var unconfirmedDict = LatestUnconfirmedEditsByField;
                if (Properties == null) GenerateReflectionData();

                
                //var lastUpdated = State.LastUpdated;
                //var diffUpdated = data.DataDictionary.LastUpdated;

                // isLocal => Diff applied to local live or shadow from local live or shadow; just apply everything as requested
                var isLocal = isResponse == null;;
                // Did message come from server (I am client)
                var isFromServer = (isResponse ?? false) == true;
                // Did message come from client (I am server)
                var isFromClient = (isResponse ?? true) == false;

                // Local overrides
                // Patch overrides
                // Newest overrides
                foreach (var prop in Properties.Where(p => data.DiffFields.ContainsKey(p.Name)))
                {
                    var peerModified = data.DiffFields[prop.Name];
                    // You might worry that the client LocalModifiedTimes only applies to this session, so could get overridden by an older
                    // copy from the server. However if we have synced with the server the server must have our latest modified time stored,
                    // so will reject any changes from other clients that were made before that time.
                    var localModified = unconfirmedDict.ContainsKey(prop.Name) ? unconfirmedDict[prop.Name] : DateTime.MinValue;

                    if( isFromClient )
                        localModified = ServerFieldsUpdated.ContainsKey(prop.Name) ? ServerFieldsUpdated[prop.Name] : DateTime.MinValue;

                    var priorityToLatest = Attribute.IsDefined(prop, typeof(DiffSyncPriorityToLatestChange));
                    var priorityToClient = Attribute.IsDefined(prop, typeof(DiffSyncPriorityToClientAttribute));
                    var priorityToServer = Attribute.IsDefined(prop, typeof(DiffSyncPriorityToServerAttribute));
                    var isInk = Attribute.IsDefined(prop, typeof(DiffSyncInkAttribute));
                    var isUnionDistinct = Attribute.IsDefined(prop, typeof(DiffSyncUnionDistinctAttribute));
                    var isImportant = Attribute.IsDefined(prop, typeof(DiffSyncImportant));

                    if (!priorityToClient && !priorityToServer) priorityToLatest = true;

                    var isLocalMoreRecent = (peerModified < localModified );

                    if (isUnionDistinct && prop.PropertyType == typeof(List<int>))
                    {
                        // If it's ink do a merge of the two; timing doesn't matter
                        var diffStrokes = prop.GetValue(data.DataDictionary) as List<int>;
                        var stateStrokes = prop.GetValue(State) as List<int>;

                        if (stateStrokes == null && diffStrokes == null)
                            stateStrokes = null;
                        else if (stateStrokes == null)
                            stateStrokes = diffStrokes;
                        else if (diffStrokes == null)
                            diffStrokes = stateStrokes;
                        else
                        {
                            stateStrokes = stateStrokes
                                .Union(diffStrokes)
                                .Distinct().ToList();
                        }

                        if (stateStrokes != null)
                        {
                            prop.SetValue(State, stateStrokes);
                        }
                        else
                        {
                            prop.SetValue(State, null);
                        }
                    }
                    else if (isInk && prop.PropertyType == typeof(byte[]))
                    {
                        // If it's ink do a merge of the two; timing doesn't matter
                        var diffBytes = prop.GetValue(data.DataDictionary) as byte[];
                        var stateBytes = prop.GetValue(State) as byte[];

                        var diffStrokes = ByteToStrokes(diffBytes);
                        var stateStrokes = ByteToStrokes(stateBytes);
                        var patchRemovedStrokes = data.DataDictionary.DiffSyncRemovedStrokes.Select(t => new Point(t.Item1, t.Item2)).ToList();

                        if (stateStrokes == null && diffStrokes == null)
                            stateStrokes = null;
                        else if (stateStrokes == null)
                            stateStrokes = diffStrokes;
                        else if (diffStrokes == null)
                            diffStrokes = stateStrokes;
                        else
                        {
                            stateStrokes = stateStrokes
                                .Union(diffStrokes)
                                .GroupBy(s => s.Key)
                                .ToDictionary(s => s.Key, s => s.OrderByDescending(st => st.Value.StylusPoints.Count).Select(st => st.Value).First());
                        }

                        if (stateStrokes != null)
                        {
                            var sc = new StrokeCollection(stateStrokes.Where(r => !patchRemovedStrokes.Any(q => (q - r.Key).Length < 0.5)).Select(s => s.Value));
                            prop.SetValue(State, StrokeToBytes(sc));
                        }
                        else
                        {
                            prop.SetValue(State, null);
                        }
                    }
                    else if (isShadow || isLocal)
                        prop.SetValue(State, prop.GetValue(data.DataDictionary));
                    else if (!isLocalMoreRecent && priorityToLatest)
                        prop.SetValue(State, prop.GetValue(data.DataDictionary));
                    else if (isFromServer && priorityToServer)
                        prop.SetValue(State, prop.GetValue(data.DataDictionary));
                    else if (isFromClient && priorityToClient)
                        prop.SetValue(State, prop.GetValue(data.DataDictionary));
                    else
                    {
                        Console.WriteLine("Overriding " + prop.Name + " ");
                    }

                    if(isImportant && !(peerModified == DateTime.MinValue && localModified == DateTime.MinValue))
                        Console.WriteLine(prop.Name + " : Peer=" + peerModified.ToString("s") + " Local=" + localModified.ToString("s"));
                }

                foreach (var prop in Fields.Where(p => data.DiffFields.ContainsKey(p.Name)))
                {
                    var peerModified = data.DiffFields[prop.Name];
                    // You might worry that the client LocalModifiedTimes only applies to this session, so could get overridden by an older
                    // copy from the server. However if we have synced with the server the server must have our latest modified time stored,
                    // so will reject any changes from other clients that were made before that time.
                    var localModified = unconfirmedDict.ContainsKey(prop.Name) ? unconfirmedDict[prop.Name] : DateTime.MinValue;

                    var priorityToLatest = Attribute.IsDefined(prop, typeof(DiffSyncPriorityToLatestChange));
                    var priorityToClient = Attribute.IsDefined(prop, typeof(DiffSyncPriorityToClientAttribute));
                    var priorityToServer = Attribute.IsDefined(prop, typeof(DiffSyncPriorityToServerAttribute));
                    var isImportant = Attribute.IsDefined(prop, typeof(DiffSyncImportant));

                    if (!priorityToClient && !priorityToServer) priorityToLatest = true;

                    if (isFromClient)
                        localModified = ServerFieldsUpdated.ContainsKey(prop.Name) ? ServerFieldsUpdated[prop.Name] : DateTime.MinValue;

                    var isLocalMoreRecent = (peerModified < localModified);

                    Console.WriteLine(prop.Name + " : Peer=" + peerModified.ToString("s") + " Local=" + localModified.ToString("s"));

                    if ( isShadow || isLocal ) // Shadow changes must always be applied
                        prop.SetValue(State, prop.GetValue(data.DataDictionary));
                    else if (!isLocalMoreRecent && priorityToLatest)
                        prop.SetValue(State, prop.GetValue(data.DataDictionary));
                    else if (isFromServer && priorityToServer)
                        prop.SetValue(State, prop.GetValue(data.DataDictionary));
                    else if (isFromClient && priorityToClient)
                        prop.SetValue(State, prop.GetValue(data.DataDictionary));
                    else if (!isFromServer && !isFromClient)
                        prop.SetValue(State, prop.GetValue(data.DataDictionary));
                    else
                    {
                        Console.WriteLine("Overriding " + prop.Name + " ");
                    }
                    if (isImportant && !(peerModified == DateTime.MinValue && localModified == DateTime.MinValue))
                        Console.WriteLine(prop.Name + " : Peer=" + peerModified.ToString("s") + " Local=" + localModified.ToString("s"));
                }
            }

            /// <summary>
            /// If set to true this patcher will ignore any attributes with [DiffSyncMessageOnly] set (used to ignore certain fields when syncing)
            /// </summary>
            public bool IgnoreMessageOnlyAttributes = false;
            public bool ImportantOnly = false;
            public Diff GetDiff(int version, T o, bool doTimestamp=false)
            {
                if (Properties == null) GenerateReflectionData();
                var unconfirmedDict = LatestUnconfirmedEditsByField;

                var timestamp = (doTimestamp ? DateTime.Now : DateTime.MinValue);

                var aData = GetStateData();

                var bData = o;

                T diffData = new T();

                var diffFields = new Dictionary<string, DateTime>();
                if (bData != null)
                {
                    foreach (var p in Properties.Where(p => !(p.GetValue(aData)?.Equals(p.GetValue(bData)) ?? (p.GetValue(bData) == null))))
                    {
                        var isInk = Attribute.IsDefined(p, typeof(DiffSyncInkAttribute));
                        var isMessageOnly = Attribute.IsDefined(p, typeof(DiffSyncMessageOnlyAttribute));
                        var isImportant = Attribute.IsDefined(p, typeof(DiffSyncImportant));
                        var isUnionDistinct = Attribute.IsDefined(p, typeof(DiffSyncUnionDistinctAttribute));

                        if (isMessageOnly && IgnoreMessageOnlyAttributes) continue;
                        if (!isImportant && ImportantOnly) continue;

                        if ( isUnionDistinct )
                        {
                            var aBytes = p.GetValue(aData) as List<int>;
                            var bBytes = p.GetValue(bData) as List<int>;
                            if (aBytes != null && bBytes == null)
                            {
                                diffFields.Add(p.Name, unconfirmedDict.ContainsKey(p.Name) ? unconfirmedDict[p.Name] : timestamp);
                                p.SetValue(diffData, aBytes);
                            }
                            else if (aBytes == null && bBytes != null)
                            {
                                diffFields.Add(p.Name, unconfirmedDict.ContainsKey(p.Name) ? unconfirmedDict[p.Name] : timestamp);
                                p.SetValue(diffData, bBytes);
                            }
                            else if (aBytes == null && bBytes == null)
                            {
                            }
                            else
                            {
                                if( aBytes.Count != bBytes.Count || aBytes.Except(bBytes).Count() != 0 )
                                {
                                    var res = aBytes.Union(bBytes).Distinct().ToList();
                                    p.SetValue(diffData, res);
                                    diffFields.Add(p.Name, unconfirmedDict.ContainsKey(p.Name) ? unconfirmedDict[p.Name] : timestamp);
                                }
                            }
                        }
                        else if ( isInk)
                        {
                            var aBytes = p.GetValue(aData) as byte[];
                            var bBytes = p.GetValue(bData) as byte[];
                            if (aBytes != null && bBytes == null)
                            {
                                diffFields.Add(p.Name, unconfirmedDict.ContainsKey(p.Name) ? unconfirmedDict[p.Name] : timestamp);
                                p.SetValue(diffData, aBytes);
                            }
                            else if (aBytes == null && bBytes != null)
                            {
                                diffFields.Add(p.Name, unconfirmedDict.ContainsKey(p.Name) ? unconfirmedDict[p.Name] : timestamp);
                                p.SetValue(diffData, bBytes);
                            }
                            else if (aBytes == null && bBytes == null )
                            {
                            }
                            else
                            {
                                // We need to do a strokes - strokes comparison
                                var aInk = ByteToStrokes(aBytes);
                                var bInk = ByteToStrokes(bBytes);
                                if (aInk.Count != bInk.Count || aInk.Keys.Except(bInk.Keys).Count() > 0)
                                {
                                    var setStrokes = aInk.Where(a => !bInk.ContainsKey(a.Key) || bInk[a.Key].StylusPoints.Count < a.Value.StylusPoints.Count).ToList();
                                    setStrokes.AddRange(bInk.Where(b => aInk.ContainsKey(b.Key) && aInk[b.Key].StylusPoints.Count < b.Value.StylusPoints.Count));
                                    var deleteStrokes = bInk.Where(b => !aInk.ContainsKey(b.Key)).ToDictionary(s => s.Key, s => s.Value);
                                    diffData.DiffSyncRemovedStrokes.Clear();
                                    diffData.DiffSyncRemovedStrokes.AddRange(deleteStrokes.Keys.Select(t=>new Tuple<double,double>(t.X, t.Y)).ToList());
                                    p.SetValue(diffData, StrokeToBytes(new StrokeCollection(setStrokes.Select(s => s.Value))));
                                    diffFields.Add(p.Name, unconfirmedDict.ContainsKey(p.Name) ? unconfirmedDict[p.Name] : timestamp);
                                }
                            }
                        }
                        else
                        {
                            diffFields.Add(p.Name, unconfirmedDict.ContainsKey(p.Name) ? unconfirmedDict[p.Name] : timestamp);
                            p.SetValue(diffData, p.GetValue(aData));
                        }
                    }
                    foreach (var p in Fields.Where(p => !(p.GetValue(aData)?.Equals(p.GetValue(bData)) ?? (p.GetValue(bData) == null))))
                    {
                        var isImportant = Attribute.IsDefined(p, typeof(DiffSyncImportant));
                        var isMessageOnly = Attribute.IsDefined(p, typeof(DiffSyncMessageOnlyAttribute));
                        if (isMessageOnly && IgnoreMessageOnlyAttributes) continue;
                        if (!isImportant && ImportantOnly) continue;

                        diffFields.Add(p.Name, unconfirmedDict.ContainsKey(p.Name) ? unconfirmedDict[p.Name] : timestamp);
                        var val = p.GetValue(aData);
                        p.SetValue(diffData, val) ;
                    }
                }
                else
                    return null;

                if (diffFields.Count == 0) return null;

                return new Diff(diffData)
                {
                    Version = version,
                    DiffFields = diffFields
                };
            }

        }
        [DataContract]
        public class Syncer : DiffSync.NET.ProtocolStateMachine<Patcher, Diff, T>
        {
            [DataMember]
            public Guid SessionGuid { get; set; } = Guid.NewGuid();
            [DataMember]
            public Guid ObjectGuid { get; set; }
            [DataMember]
            public DateTime LastMessageRecvTime { get; set; } = DateTime.MinValue;
            [DataMember]
            public DateTime LastMessageSendTime { get; set; } = DateTime.MinValue;
            [DataMember]
            public DateTime LastDiffTime { get; set; } = DateTime.MinValue;
            [DataMember]
            public int StartRevision { get; set; }

            ///// <summary>
            ///// Note this does not alter the last modified values, so should not cause data loss. If a field simply wans't syncing before
            ///// just because it isn't being send properly this will not erase that unconfirmed edit/
            ///// 
            ///// The modified timestamps also are not lost in this process
            ///// </summary>
            ///// <param name="newShadow"></param>
            //public void ApplyNewShadow(T newShadow)
            //{
            //    // This isn't a good approach because it means dropping the unconfirmed diffs, and 
            //    throw new Exception("Do not apply new shadow; instead dump and recreate a new syncer if a syncer faults.");
            //    Shadow.StateObject.State.CopyStateFrom(newShadow);
            //    BackupShadow.StateObject.State.CopyStateFrom(newShadow);
            //    LastNewShadow = DateTime.Now;

            //    // TODO: Unconfirmed edits should actually be app

            //    UnconfirmedEdits.Diffs.Clear();
            //}
            /// <summary>
            /// This is the object that the client has set to the syncer to indicate that we need to incorporate changes from this
            /// </summary>
            public T LatestClientObject { internal get; set; }
            private T PreviousClientObject = null;
            /// <summary>
            /// Warning: You can only be sure that this object doesn't have changes from the server that might be about to get overridden
            /// by our own changes when we have no UnconfirmedEdits. The LiveObject may be updated with the server's changes while changes 
            /// we are sending are still going through. Don't propagate changes back from this object until HasUnconfirmedChanges = false
            /// </summary>
            public T LiveObject => Live.StateObject.State;

            /// <summary>
            /// Set a copy of the state retrieved from a second channel here, and the syncer will flag itself completed if it matches this and there is nothing left to do.
            /// This is a very solid method of validating that changes have gone through as expected, and ensures a client does not slowly accumulate massive numbers of
            /// mostly idle background syncers.
            /// </summary>
            public T ServerCheckCopy { get; set; }
            /// <summary>
            /// True if there is no diff result between the ServerCheckCopy and the Live. If this is true and all messages are sent and everything is quiet then the syncer
            /// has done its job and can be cleanly finished and closed.
            /// </summary>
            public bool IsSynced { get;  set; } = false;

            public object FileWriteLock { get; private set; } = new object();
            public List<string> WriteCommands = new List<string>();
            public Syncer() { } // Required for deserialization, but should not be used in normal usage

            /// <summary>
            /// The live object here should not be an object that will be used elsewhere in the application; it should only get updated after this 
            /// </summary>
            /// <param name="live"></param>
            /// <param name="shadow"></param>
            public Syncer(Guid objectGuid, T live, T shadow, int startRevision)
            {
                ObjectGuid = objectGuid;
                StartRevision = startRevision;

                // This will only be modified internally and sent by the patcher
                var livePatcher = new Patcher("Live", new T().CopyStateFrom(live)) { UnconfirmedEdits = UnconfirmedEdits };
                var shadowPatcher = new Patcher("Shadow", new T().CopyStateFrom(shadow)) { UnconfirmedEdits = UnconfirmedEdits };
                var backupShadowPatcher = new Patcher("Backup", new T().CopyStateFrom(shadow)) { UnconfirmedEdits = UnconfirmedEdits };

                Console.WriteLine("Syncing " + objectGuid.ToString());
                // Get the initial set of differences:

                Initialize(livePatcher, shadowPatcher, backupShadowPatcher);
            }
            public void SetServerTimestamps(Dictionary<string, DateTime> serverFieldsUpdated)
            {
                // This will only be modified internally and sent by the patcher
                LivePatcher.SetServerTimestamps(serverFieldsUpdated);
                ShadowPatcher.SetServerTimestamps(serverFieldsUpdated);
                BackupShadowPatcher.SetServerTimestamps(serverFieldsUpdated);
                LivePatcher.UnconfirmedEdits = UnconfirmedEdits;
                ShadowPatcher.UnconfirmedEdits = UnconfirmedEdits;
                BackupShadowPatcher.UnconfirmedEdits = UnconfirmedEdits;
                // This is the server updating 
            }
            /// <summary>
            /// Copies for ease of access to Reflection based functions
            /// </summary>
            /// 
            private Patcher LivePatcher => Live.StateObject;
            private Patcher ShadowPatcher => Shadow.StateObject;
            private Patcher BackupShadowPatcher => BackupShadow.StateObject;

            //[DataMember]
            //public int ShadowStartRevision { get; set; } = -1;


            /// <summary>
            /// Keeps a track of when this client has updated which fields. Used to be able to reject or accept individual fields based on time changed
            /// </summary>
            // No need to store the peer / server's update times here, they will come through with the diffs

            /// <summary>
            /// This is a client cycle, so we need to decide whether to send a message based on whether there are changes, whether we just sent a message, etc
            /// </summary>
            /// <param name="newState">An updated state we want to send to the server (or null if just checking for timers)</param>
            /// <param name="alwaysGenerateMessage"></param>
            /// <param name="msgReceived"></param>
            /// <returns></returns>
            public (bool PeerVersionChanged, MessagePacket ReturnMessage) ClientMessageCycle(bool alwaysGenerateMessage = true, MessagePacket msgReceived = null)
            {
                // This will apply the changes to the shadow and the live from the server:
                var messageChangedPeerVersion = msgReceived != null && ReadMessageCycle(msgReceived?.Message);

                var generateMessage = true;

                Diff shadowDiff = null;
                {
                    //var localDiff = DiffApplyLive();

                    //// Save modified field timestamps (this would not be appropriate in the server, however in the client it is fine,
                    //// since this data is from the client not from the server receiving an update from another client and recording that time)

                    //// On the server side the FieldLastModified data comes from a separate table which stores the latest timestamp for each objectguid-field
                    //foreach (var field in localDiff.DiffFields)
                    //{
                    //    if (FieldsLastModified.FieldTimestamps.ContainsKey(field.Key))
                    //        FieldsLastModified.FieldTimestamps[field.Key] = field.Value;
                    //    else
                    //        FieldsLastModified.FieldTimestamps.Add(field.Key, field.Value);
                    //}

                    // First generate shadow diffs that are in response to the server:
                     shadowDiff = DiffApplyShadow(doTimestamp:true);

                    bool patchedLive = false;
                    if (LatestClientObject != null )
                    {
                        var latestClientObj = new T().CopyStateFrom(LatestClientObject);
                        LatestClientObject = null;
                        if (PreviousClientObject != null)
                        {
                            var previousClientUpdate = new T().CopyStateFrom(PreviousClientObject);

                            var clientPatch = new Patcher("Client diff", latestClientObj);
                            var cDiff = clientPatch.GetDiff(0, previousClientUpdate, doTimestamp:true);

                            var livePatch = new Patcher("Client to Live patch", LiveObject);
                            livePatch.PrintDifferences(cDiff, LiveObject);
                            livePatch.Apply(cDiff, null, false);

                            // Now generate a diff that is purely in response to changes made locally
                            var liveDiff = DiffApplyShadow(doTimestamp: true);

                            if (shadowDiff != null)
                            {
                                foreach (var d in shadowDiff.DiffFields.ToList())
                                    if (cDiff.DiffFields.ContainsKey(d.Key))
                                        shadowDiff.DiffFields[d.Key] = cDiff.DiffFields[d.Key];
                            }

                            if( liveDiff != null )
                            {
                                foreach (var d in liveDiff.DiffFields.ToList())
                                    if (cDiff.DiffFields.ContainsKey(d.Key))
                                        liveDiff.DiffFields[d.Key] = cDiff.DiffFields[d.Key];
                            }

                            patchedLive = true;
                        }
                        else
                        {
                            LiveObject.CopyStateFrom(latestClientObj);

                            var liveDiff = DiffApplyShadow(doTimestamp:true);

                            if (liveDiff != null)
                            {
                                foreach (var d in liveDiff.DiffFields.Keys.ToList())
                                    liveDiff.DiffFields[d] = DateTime.Now;
                            }

                        }

                        PreviousClientObject = latestClientObj;
                    }

                    if ( shadowDiff != null || HasUnconfirmedEdits || (DateTime.Now - LastMessageSendTime).TotalSeconds > 30 )
                    {
                        if (LastMessageSendTime.Year >= 2019 && (DateTime.Now - LastMessageSendTime).TotalHours > 15)
                        {
                            throw new Exception("Have an unsynced item after 6 hours without sending a message.");
                            // IsExpired = true;
                        }
                        else if (IsWaitingForMessage && (DateTime.Now - LastMessageSendTime).TotalSeconds < 15)
                        {
                            // If we are waiting for a message don't spam the state.
                            generateMessage = false;
                        }
                    }
                }

                if ( generateMessage || messageChangedPeerVersion || alwaysGenerateMessage )
                {
                    // MakeMessageCycle does another diff; don't do this if we have already diffed while looking for changes
                    var msg = GenerateMessage(msgReceived?.Message);

                    if (msg != null)
                    {

                        return (messageChangedPeerVersion, new MessagePacket(SessionGuid, ObjectGuid, msg, 0));
                    }
                        
                }
                return (messageChangedPeerVersion, null);
            }
            //public const bool USEMESSAGEPACK = true;
            public string Serialize() => Newtonsoft.Json.JsonConvert.SerializeObject(this);
            //public byte[] Serialize(Syncer s = null)
            //{
            //    if( s == null )
            //    {
            //        s = this;
            //    }
            //    var res = MessagePack.MessagePackSerializer.Serialize(s, MessagePack.Resolvers.StandardResolverAllowPrivate.Options);
            //    return res;
            //}
            //public static Syncer DeserializeJSONStr(string s) => Deserialize(System.Text.ASCIIEncoding.ASCII.GetBytes(s));
            public static Syncer Deserialize(string s) => Newtonsoft.Json.JsonConvert.DeserializeObject<Syncer>(s);
            //public static Syncer Deserialize(byte[] s)
            //{
            //    var b = new System.Buffers.ReadOnlySequence<byte>(s);
            //    return MessagePack..Deserialize< Syncer>(byteSequence: b, MessagePackSerializerOptions.Standard, (new System.Threading.CancellationTokenSource()).Token);
            //}
        }


        /// <summary>
        /// A diff object, containing changes to be applied. In the reflection-based syncer this is done with an item with the data and a list of
        /// fields which contain changed data.
        /// </summary>
        [DataContract]
        public class Diff : DiffSync.NET.IDiff
        {
            [DataMember]
            public int Version { get; set; }
            /// <summary>
            /// This only contains the changes; this is easier than a dictionary because it maintains the type data
            /// </summary>
            [DataMember]
            public T DataDictionary { get; set; }
            /// <summary>
            /// Each diff field now has a timestamp indicating when the change was first entered in via the LiveObject, so that the patcher
            /// can tell which should take priority
            /// </summary>
            [DataMember]
            public Dictionary<string, DateTime> DiffFields { get; internal set; }
            public Diff(T im)
            {
                DataDictionary = im;
            }
        }
        /// <summary>
        /// A wrapper around the diff data showing where it addressed and how to initialize the state, and any other implementation specific info
        /// </summary>
        [DataContract]
        public class MessagePacket
        {
            public MessagePacket(Guid sessionGuid, Guid objectGuid, DiffSync.NET.Message<Diff> message, int startRevision)
            {
                SessionGuid = sessionGuid;
                ObjectGuid = objectGuid;
                Message = message;
                StartRevision = startRevision;

            }
            [DataMember]
            public DiffSync.NET.Message<Diff> Message { get; set; }
            [DataMember]
            public Guid SessionGuid { get; set; }
            [DataMember]
            public Guid ObjectGuid { get; set; }
            [DataMember]
            public bool IsBackgroundMessage { get; set; } = true;
            /// <summary>
            /// Sets the shadow version that the server should set to start with so that it will equal the shadow version on the client, which should match the server object.
            /// (TODO: Shadow version checksum)
            /// </summary>
            [DataMember]
            public int StartRevision { get; internal set; } = 0;
            /// <summary>
            /// If the server errors out it will set the exception here:
            /// </summary>
            [DataMember]
            public Exception ServerError { get; set; } = null;
            /// <summary>
            /// If the client sends a message with this set to true the server will cleanly close the syncer on its end. This is like an EOF flag for the protocol
            /// </summary>
            [DataMember]
            public bool ClientCompleted { get; set; } = false;
            /// <summary>
            /// If the server sends a message with this set to true the server will cleanly close the syncer on its end. This is like an EOF flag for the protocol
            /// </summary>
            [DataMember]
            public bool ServerCompleted { get; set; } = false;
            ///// <summary>
            ///// Not part of the core DiffSync protocol, but setting this will trigger the server to throw out its shadow and start from a new base
            ///// </summary>
            //[DataMember]
            //public int NewShadowRevision { get; internal set; } = 0;
            /// <summary>
            /// Making sure the initial shadow is the exact correct one
            /// </summary>
            //[DataMember]
            //public int StartShadowRevision { get; internal set; } = 0;
        }
    }
}
