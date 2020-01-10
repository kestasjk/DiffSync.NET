using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Ink;

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
        public static Syncer Create(Guid objectGuid, T rootLiveItem, T initialShadowItem = null) => new Syncer(objectGuid, rootLiveItem, initialShadowItem ?? new T());
        
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

        public class Caller { 
            public string a;
            public string b;
            public int c; 
        }
        private static ImmutableDictionary<string, Caller> Savers = ImmutableDictionary<string, Caller>.Empty;

        public static async Task<(List<Guid> Sent, List<Guid> Received, List<Guid> Failed, List<Guid> Completed)> SyncDictionary(Dictionary<Guid, Syncer> syncers, Func<MessagePacket, Task<MessagePacket>> sendPacket, DirectoryInfo cacheFolder,
      [CallerMemberName] string member = "",
      [CallerFilePath] string path = "",
      [CallerLineNumber] int line = 0)
        {
            var hello = new Caller()
            {
                a = member,
                b = path,
                c = line
            };

            return await SyncDictionary(syncers, sendPacket, async (syncer, json) => await Task.Run(() =>
            {
                var filename = System.IO.Path.Combine(cacheFolder.FullName, syncer.ObjectGuid.ToString() + ".json");
                var myHello = hello;

                lock(syncer.FileWriteLock)
                {
                    // For some reason without this lock you can get "file is in use" errors here, even though I can't see how it can happen because it's awaited
                    // before it moves onto the next cycle.. something weird about this

                    //syncer.WriteCommands.Add(json);
                    //syncer.WriteCallers.Add(filename, myHello);
                    if(json == null )
                        System.IO.File.Delete(filename);
                    else
                        System.IO.File.WriteAllText(filename, json);
                    //syncer.WriteCallers.Remove(filename);
                }
            }));
        }
        public static async Task<(List<Guid> Sent, List<Guid> Received, List<Guid> Failed, List<Guid> Completed)> SyncDictionaryServer(Dictionary<Guid, Syncer> syncers, Func<MessagePacket, Task<MessagePacket>> sendPacket, DirectoryInfo cacheFolder,
      [CallerMemberName] string member = "",
      [CallerFilePath] string path = "",
      [CallerLineNumber] int line = 0)
        {
            var hello = new Caller()
            {
                a = member,
                b = path,
                c = line
            };

            return await SyncDictionary(syncers, sendPacket, async (syncer, json) => await Task.Run(() =>
            {
                var filename = System.IO.Path.Combine(cacheFolder.FullName, syncer.SessionGuid.ToString() + "_" + syncer.ObjectGuid.ToString() + ".json");
                var myHello = hello;

                lock (syncer.FileWriteLock)
                {
                    // For some reason without this lock you can get "file is in use" errors here, even though I can't see how it can happen because it's awaited
                    // before it moves onto the next cycle.. something weird about this

                    //syncer.WriteCommands.Add(json);
                    //syncer.WriteCallers.Add(filename, myHello);
                    System.IO.File.WriteAllText(filename, json);
                    //syncer.WriteCallers.Remove(filename);
                }
            }));
        }
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
        public static async Task<(List<Guid> Sent, List<Guid> Received, List<Guid> Failed, List<Guid> Completed)> SyncDictionary(Dictionary<Guid, Syncer> syncers, Func<MessagePacket, Task<MessagePacket>> sendPacket, Func<Syncer, string, Task> saveToDisk=null)
        {
            var updated = new List<Guid>();
            var sent = new List<Guid>();
            var errored = new List<Guid>();
            var completed = new List<Guid>();

            var tasks = new Dictionary<Guid, Task<MessagePacket>>();
            var completionMessageTasks = new Dictionary<Guid, Task<MessagePacket>>();
            foreach (var i in syncers.ToList())
            {
                if (i.Value.ServerCheckCopy != null)
                {
                    // A new server check copy to check against.

                    var serverCheckDiff = new Patcher(i.Value.ServerCheckCopy).GetDiff(0, i.Value.LiveObject);

                    if(serverCheckDiff != null)
                    {

                    }

                    // No differences with the server; we are synced up! (Don't act yet as MessageCycle() may still emit a message)
                    i.Value.IsSynced = ((serverCheckDiff?.DiffFields?.Count ?? 0 ) == 0);
                    //i.Value.ServerCheckCopy = null;
                }

                var msg = i.Value.ClientMessageCycle(); // This will generate a message if something has changed or is still being synced, handles time outs and repeats etc
                
                if (i.Value.IsSynced && (msg.ReturnMessage?.Message == null || msg.ReturnMessage.Message.Diffs.Count == 0) )// && (DateTime.Now - i.Value.LastDiffTime).TotalMinutes > 15) // Don't bother waiting; if we're synced we're synced
                {
                    // We're synced, there's nothing to send, it has been quiet for a while
                    completed.Add(i.Key);

                    // Don't bother letting the server know we are synced; instead just let it time out and get garbage collected
                    //completionMessageTasks.Add(i.Key, sendPacket(new MessagePacket(i.Value.SessionGuid, i.Value.ObjectGuid, null) { ClientCompleted = true })); // No revision needed to complete
                }
                else if (msg.ReturnMessage != null && (msg.ReturnMessage.Message.Diffs.Count > 0 || (DateTime.Now - i.Value.LastMessageSendTime).TotalSeconds > -1))
                {
                    i.Value.LastMessageSendTime = DateTime.Now;

                    if ((msg.ReturnMessage.Message?.Diffs?.Count ?? 0) != 0) i.Value.LastDiffTime = DateTime.Now;

                    tasks.Add(i.Key, sendPacket(msg.ReturnMessage));
                }
            }

            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks.Values.ToArray());

                foreach (var t in tasks)
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
            public Patcher()
            {

            }
            public Patcher(T state)
            {
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
                        Properties = typeof(T).GetProperties().Where(prop => Attribute.IsDefined(prop, typeof(DiffSyncAttribute))).ToList();
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
            public void SetStateData(T state) => State.CopyStateFrom(state);


            public void Apply(Diff data, bool? isResponse, bool isShadow)
            {
                if (Properties == null) GenerateReflectionData();
                
                var lastUpdated = State.LastUpdated;
                var diffUpdated = data.DataDictionary.LastUpdated;
                var isDiffMoreRecent = diffUpdated > lastUpdated;

                var isFromServer = (isResponse ?? false) == true;
                var isFromClient = (isResponse ?? true) == false;

                // Local overrides
                // Patch overrides
                // Newest overrides
                foreach (var prop in Properties.Where(p => data.DiffFields.Contains(p.Name)))
                {
                    var priorityToLatest = Attribute.IsDefined(prop, typeof(DiffSyncPriorityToLatestChange));
                    var priorityToClient = Attribute.IsDefined(prop, typeof(DiffSyncPriorityToClientAttribute));
                    var priorityToServer = Attribute.IsDefined(prop, typeof(DiffSyncPriorityToServerAttribute));

                    if (!priorityToClient && !priorityToServer) priorityToLatest = true;
                    //if (prop.Name == "Strokes")
                    //{
                    //    var aInk = ByteToStrokes(Strokes);
                    //    var patch = ByteToStrokes(data.DataDictionary.Strokes);
                    //    var patchRemovedStrokes = data.DataDictionary.DiffSyncRemovedStrokes;

                    //    var resInk = aInk;
                    //    if (aInk == null && patch == null)
                    //        resInk = null;
                    //    else if (aInk == null)
                    //        resInk = patch;
                    //    else if (patch == null)
                    //        resInk = aInk;
                    //    else
                    //    {
                    //        resInk = aInk.Union(patch).GroupBy(s => s.Key).ToDictionary(s => s.Key, s => s.OrderByDescending(st => st.Value.StylusPoints.Count).Select(st => st.Value).First());
                    //    }

                    //    if (resInk != null)
                    //    {
                    //        var sc = new StrokeCollection(resInk.Where(r => !patchRemovedStrokes.Any(q => (q - r.Key).Length < 0.5)).Select(s => s.Value));
                    //        prop.SetValue(this, StrokeToBytes(sc));
                    //    }
                    //    else
                    //    {
                    //        prop.SetValue(this, null);
                    //    }
                    //}
                    //else
                    if( isShadow )
                        prop.SetValue(State, prop.GetValue(data.DataDictionary));
                    else if( isDiffMoreRecent && priorityToLatest )
                        prop.SetValue(State, prop.GetValue(data.DataDictionary));
                    else if ( isFromServer && priorityToServer )
                        prop.SetValue(State, prop.GetValue(data.DataDictionary));
                    else if ( isFromClient && priorityToClient )
                        prop.SetValue(State, prop.GetValue(data.DataDictionary));
                    else if ( !isFromServer && !isFromClient )
                        prop.SetValue(State, prop.GetValue(data.DataDictionary));
                }

                foreach (var field in Fields.Where(p => data.DiffFields.Contains(p.Name)))
                {

                    var priorityToLatest = Attribute.IsDefined(field, typeof(DiffSyncPriorityToLatestChange));
                    var priorityToClient = Attribute.IsDefined(field, typeof(DiffSyncPriorityToClientAttribute));
                    var priorityToServer = Attribute.IsDefined(field, typeof(DiffSyncPriorityToServerAttribute));

                    if (!priorityToClient && !priorityToServer) priorityToLatest = true;

                    if (isShadow)
                        field.SetValue(State, field.GetValue(data.DataDictionary));
                    else if (isDiffMoreRecent && priorityToLatest)
                        field.SetValue(State, field.GetValue(data.DataDictionary));
                    else if (isFromServer && priorityToServer)
                        field.SetValue(State, field.GetValue(data.DataDictionary));
                    else if (isFromClient && priorityToClient)
                        field.SetValue(State, field.GetValue(data.DataDictionary));
                    else if (!isFromServer && !isFromClient)
                        field.SetValue(State, field.GetValue(data.DataDictionary));
                }
            }

            public Diff GetDiff(int version, T o)
            {
                if (Properties == null) GenerateReflectionData();

                var aData = GetStateData();

                var bData = o;

                T diffData = new T();

                var hasDiff = false;
                var diffFields = new List<string>();
                if (bData != null)
                {
                    //if (aData.Strokes != null && bData.Strokes == null)
                    //{
                    //    diffFields.Add("Strokes");
                    //    diffData.Strokes = aData.Strokes;
                    //}
                    //else if (aData.Strokes == null && bData.Strokes != null)
                    //{
                    //    diffFields.Add("Strokes");
                    //    diffData.DiffSyncRemovedStrokes = new List<Point>(ByteToStrokes(bData.Strokes).Keys);
                    //}
                    //else if (aData.Strokes == null && bData.Strokes == null)
                    //{
                    //}
                    //else
                    //{
                    //    // We need to do a strokes - strokes comparison
                    //    var aInk = ByteToStrokes(aData.Strokes);
                    //    var bInk = ByteToStrokes(bData.Strokes);
                    //    if (aInk.Count != bInk.Count || aInk.Keys.Except(bInk.Keys).Count() > 0)
                    //    {
                    //        var setStrokes = aInk.Where(a => !bInk.ContainsKey(a.Key) || bInk[a.Key].StylusPoints.Count < a.Value.StylusPoints.Count).ToList();
                    //        setStrokes.AddRange(bInk.Where(b => aInk.ContainsKey(b.Key) && aInk[b.Key].StylusPoints.Count < b.Value.StylusPoints.Count));
                    //        var deleteStrokes = bInk.Where(b => !aInk.ContainsKey(b.Key)).ToDictionary(s => s.Key, s => s.Value);
                    //        diffData.DiffSyncRemovedStrokes = deleteStrokes.Keys.ToList();
                    //        diffData.Strokes = StrokeToBytes(new StrokeCollection(setStrokes.Select(s => s.Value)));
                    //        diffFields.Add("Strokes");
                    //    }
                    //}
                    //if (aData.ScannedItemVesco != null && bData.ScannedItemVesco == null)
                    //{
                    //    diffFields.Add("ScannedItemVesco");
                    //    diffData.ScannedItemVesco = aData.ScannedItemVesco;
                    //}
                    //else if (aData.ScannedItemVesco == null && bData.ScannedItemVesco != null)
                    //{
                    //    diffFields.Add("ScannedItemVesco");
                    //    diffData.ScannedItemVesco = aData.ScannedItemVesco;
                    //}
                    //else if (aData.ScannedItemVesco == null && bData.ScannedItemVesco == null)
                    //{
                    //}
                    //else
                    //{
                    //    if (aData.ScannedItemVesco.Barcode?.Contents != bData.ScannedItemVesco.Barcode?.Contents)
                    //    {
                    //        diffFields.Add("ScannedItemVesco");
                    //        diffData.ScannedItemVesco = aData.ScannedItemVesco;
                    //    }
                    //}

                    //if (aData.ScannedItemWorkOrder != null && bData.ScannedItemWorkOrder == null)
                    //{
                    //    diffFields.Add("ScannedItemWorkOrder");
                    //    diffData.ScannedItemWorkOrder = aData.ScannedItemWorkOrder;
                    //}
                    //else if (aData.ScannedItemWorkOrder == null && bData.ScannedItemWorkOrder != null)
                    //{
                    //    diffFields.Add("ScannedItemWorkOrder");
                    //    diffData.ScannedItemWorkOrder = aData.ScannedItemWorkOrder;
                    //}
                    //else if (aData.ScannedItemWorkOrder == null && bData.ScannedItemWorkOrder == null)
                    //{
                    //}
                    //else
                    //{
                    //    if (aData.ScannedItemWorkOrder.Barcode?.Contents != bData.ScannedItemWorkOrder.Barcode?.Contents)
                    //    {
                    //        diffFields.Add("ScannedItemWorkOrder");
                    //        diffData.ScannedItemWorkOrder = aData.ScannedItemWorkOrder;
                    //    }
                    //}
                    foreach (var p in Properties.Where(p => p.Name != "Strokes" && p.Name != "ScannedItemVesco" && p.Name != "ScannedItemWorkOrder").Where(p => !(p.GetValue(aData)?.Equals(p.GetValue(bData)) ?? (p.GetValue(bData) == null))))
                    {
                        diffFields.Add(p.Name);
                        p.SetValue(diffData, p.GetValue(aData));
                        hasDiff = true;
                    }
                    foreach (var p in Fields.Where(p => !(p.GetValue(aData)?.Equals(p.GetValue(bData)) ?? (p.GetValue(bData) == null))))
                    {
                        diffFields.Add(p.Name);
                        var val = p.GetValue(aData);
                        p.SetValue(diffData, val) ;
                        hasDiff = true;
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
            public Dictionary<string, Caller> WriteCallers = new Dictionary<string, Caller>();
            public Syncer() { } // Required for deserialization, but should not be used in normal usage

            /// <summary>
            /// The live object here should not be an object that will be used elsewhere in the application; it should only get updated after this 
            /// </summary>
            /// <param name="live"></param>
            /// <param name="shadow"></param>
            public Syncer(Guid objectGuid, T live, T shadow)
            {
                ObjectGuid = objectGuid;
                Initialize(new Patcher(live), new Patcher(shadow), new Patcher(new T().CopyStateFrom(shadow)));
            }

            /// <summary>
            /// This is a client cycle, so we need to decide whether to send a message based on whether there are changes, whether we just sent a message, etc
            /// </summary>
            /// <param name="newState">An updated state we want to send to the server (or null if just checking for timers)</param>
            /// <param name="alwaysGenerateMessage"></param>
            /// <param name="msgReceived"></param>
            /// <returns></returns>
            public (bool PeerVersionChanged, MessagePacket ReturnMessage) ClientMessageCycle(T newState = null, bool alwaysGenerateMessage = true, MessagePacket msgReceived = null)
            {
                var messageChangedPeerVersion = msgReceived != null && ReadMessageCycle(msgReceived?.Message);

                if( newState != null) Live.StateObject.SetStateData(newState);

                var generateMessage = true;
                var alreadyDiffed = false;

                // If we were last updated just a moment ago then wait for a short while for changes to come through.
                if ((DateTime.Now - LastMessageRecvTime).TotalSeconds < 15)
                    generateMessage = false;
                else
                {
                    DiffApplyLive();
                    var shadowDiff = DiffApplyShadow();
                    alreadyDiffed = true;
                    if (shadowDiff != null || HasUnconfirmedEdits || (DateTime.Now - LastMessageSendTime).TotalSeconds > 30)
                    {
                        if (LastMessageSendTime.Year >= 2019 && (DateTime.Now - LastMessageSendTime).TotalMinutes > 15)
                        {
                            // IsExpired = true;
                            generateMessage = false;
                        }
                        else if (IsWaitingForMessage && (DateTime.Now - LastMessageSendTime).TotalSeconds < 30)
                        {
                            // If we are waiting for a message don't spam the state.
                            generateMessage = false;
                        }
                    }
                }

                if (generateMessage || messageChangedPeerVersion || alwaysGenerateMessage)
                {
                    // MakeMessageCycle does another diff; don't do this if we have already diffed while looking for changes
                    var msg = alreadyDiffed ? GenerateMessage(msgReceived?.Message) : MakeMessageCycle(msgReceived?.Message);

                    if (msg != null)
                    {
                        return (messageChangedPeerVersion, new MessagePacket(SessionGuid, ObjectGuid, msg) { Revision = LiveObject.Revision });
                    }
                        
                }
                return (messageChangedPeerVersion, null);
            }
            public string Serialize() => Newtonsoft.Json.JsonConvert.SerializeObject(this);
            public static Syncer Deserialize(string s) => Newtonsoft.Json.JsonConvert.DeserializeObject<Syncer>(s);
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
            [DataMember]
            public List<string> DiffFields { get; internal set; }

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
            public MessagePacket(Guid sessionGuid, Guid objectGuid, DiffSync.NET.Message<Diff> message)
            {
                SessionGuid = sessionGuid;
                ObjectGuid = objectGuid;
                Message = message;
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
            public int Revision { get; set; } = 0;
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
        }
    }
}
