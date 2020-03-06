using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DiffSync.NET;
using System.Collections.Generic;
using DiffSync.NET.Reflection;
using System.Windows.Ink;
using System.Runtime.Serialization;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;

namespace Testing
{
    [TestClass]
    public class UnitTest1
    {
        /// <summary>
        /// Tests using one item; item creation, item update, ability to serialize/deserialize
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task TestSingleReflectionSyncing()
        {
            var s = new Server();
            var c = ExampleClass.Random();
            var sumBefore = c.TotalQuantity;
            var countBefore = c.SubObjects.Count;
            var sumSubBefore = c.TotalSubQuantity;
            var syncer = Factory<ExampleClass>.Create(c.Guid, c, new ExampleClass() { Guid = c.Guid });

            var dict = new Dictionary<Guid, Factory<ExampleClass>.Syncer>() { { c.Guid, syncer } };
            var di = new System.IO.DirectoryInfo(System.Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            var testDir = di.CreateSubdirectory("DiffSyncTest");
            if (testDir.Exists) testDir.Delete(recursive: true);
            testDir = di.CreateSubdirectory("DiffSyncTest");

            var results = await Factory<ExampleClass>.SyncDictionary(dict, async (p) => s.ReceiveMessage(p), di);
            syncer.ServerCheckCopy = s.ExampleDB.First().Value.First().Value;
            results = await Factory<ExampleClass>.SyncDictionary(dict, async (p) => s.ReceiveMessage(p), di);

            if (c.Revision != 1 || !syncer.IsSynced)
                throw new Exception("Revision / sync status is not as expected for new item");

            if (c.TotalQuantity != sumBefore || c.SubObjects.Count != countBefore || c.TotalSubQuantity != sumSubBefore)
                throw new Exception("Single item totals mismatch after creation");

            s.SaveToDisk(testDir);

            s.ExampleSubSyncers.Clear();
            s.ExampleSyncers.Clear();

            s.LoadFromDisk(testDir);

            {
                var serverSyncer = s.ExampleSyncers.First().Value.First().Value;
                if (s.ExampleSyncers.Count != 1 || serverSyncer.LiveObject.Guid != c.Guid || serverSyncer.LiveObject.TotalQuantity != c.TotalQuantity)
                    throw new Exception("Restoring server's state from disk failed");
            }
            syncer.LiveObject.QuantityDefault += 100.0m;
            syncer.LiveObject.LastUpdated = DateTime.Now;
            results = await Factory<ExampleClass>.SyncDictionary(dict, async (p) => s.ReceiveMessage(p), di);
            if( c.Revision != 2 || syncer.LiveObject.TotalQuantity != (100.0m + sumBefore) || syncer.IsSynced  )
                throw new Exception("Modification of an existing item failed");

            var latestRev = s.ExampleDBByRevision.Keys.Max();
            syncer.ServerCheckCopy = s.ExampleDBByRevision[latestRev];
            results = await Factory<ExampleClass>.SyncDictionary(dict, async (p) => s.ReceiveMessage(p), di);
            if (c.Revision != 2 || syncer.LiveObject.TotalQuantity != (100.0m + sumBefore) || !syncer.IsSynced)
                throw new Exception("Failure to sync after modification");

        }
        internal static ExampleClass CloneItem(ExampleClass e) => ((IReflectionSyncable<ExampleClass>)new ExampleClass()).CopyStateFrom(e);

        /// <summary>
        /// Tests using one item; item creation, item update, ability to serialize/deserialize
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task TestErrorCatching()
        {
            var s = new Server();
            var c = ExampleClass.RandomList(10);

            var sumBefore = c.Select(i => i.TotalQuantity).Sum();
            var countBefore = c.Select(i => i.SubObjects.Count).Sum();
            var sumSubBefore = c.Select(i => i.TotalSubQuantity).Sum();
            var before = c.Select(i => CloneItem(i)).ToList();
            var dict = c.ToDictionary(i => i.Guid, i => Factory<ExampleClass>.Create(i.Guid, i, new ExampleClass() { Guid = i.Guid }));

            var stage = 0;
            Func<Factory<ExampleClass>.MessagePacket, Task<Factory<ExampleClass>.MessagePacket>> errorMaker = async (p) => {


                if (p.ObjectGuid == c[0].Guid && c[0].Revision > 0)
                    p.SessionGuid = Guid.NewGuid();
                else if (p.ObjectGuid == c[1].Guid && c[1].Revision > 0)
                    p.Revision = c[1].Revision = -5;

                if (p.Message.RequestSeqNum == 0 && p.ObjectGuid == c[2].Guid)
                    return null;
                else if (p.Message.RequestSeqNum == 0 && p.ObjectGuid == c[3].Guid)
                    return null;
                else if (p.Message.RequestSeqNum == 2 && p.ObjectGuid == c[6].Guid)
                    return null;
                else if (p.Message.RequestSeqNum == 2 && p.ObjectGuid == c[7].Guid)
                    return null;

                var retMsg = s.ReceiveMessage(p);

                if (p.Message.RequestSeqNum == 0 && p.ObjectGuid == c[4].Guid)
                    return null;
                else if (p.Message.RequestSeqNum == 0 && p.ObjectGuid == c[5].Guid)
                    return null;
                else if (p.Message.RequestSeqNum == 2 && p.ObjectGuid == c[8].Guid)
                    return null;
                else if (p.Message.RequestSeqNum == 2 && p.ObjectGuid == c[9].Guid)
                    return null;

                return retMsg;
            };
            //0 Submit a message with an invalid session guid - fail
            //1 Submit a message with a non-existent revision# - fail
            //2 Generate a message but do not send it, then resend - success
            //3 Generate a message but do not send it, then resend with another change - success
            //4 Submit a message but ignore the response message and resend - success
            //5 Submit a message but ignore the response message and resend with another change - success
            //6 Create an item, then change it, then generate a message but do not send it, then resend - success
            //7 Create an item, then change it, then generate a message but do not send it, then resend with another change - success
            //8 Create an item, then change it, then generate a message but ignore the response, and resend - success
            //9 Create an item, then change it, then generate a message but ignore the response, and resend with another change - success
            var results = await Factory<ExampleClass>.SyncDictionary(dict, errorMaker);
            stage++;

            if (s.ExampleDB.ContainsKey(c[2].Guid) || !dict[c[2].Guid].HasUnconfirmedEdits)
                throw new Exception("Test 2 Failed message got through");
            if (s.ExampleDB.ContainsKey(c[3].Guid) || !dict[c[3].Guid].HasUnconfirmedEdits)
                throw new Exception("Test 3 Failed message got through");

            if (!s.ExampleDB.ContainsKey(c[4].Guid) )
                throw new Exception("Test 4 Message did not go through");
            if (!s.ExampleDB.ContainsKey(c[5].Guid) )
                throw new Exception("Test 5 Message did not go through");

            dict[c[0].Guid].LiveObject.QuantityDefault += 1.0m;
            dict[c[0].Guid].LiveObject.LastUpdated = DateTime.Now;
            dict[c[1].Guid].LiveObject.QuantityDefault += 1.0m;
            dict[c[1].Guid].LiveObject.LastUpdated = DateTime.Now;
            dict[c[3].Guid].LiveObject.QuantityDefault += 1.0m;
            dict[c[3].Guid].LiveObject.LastUpdated = DateTime.Now;
            dict[c[5].Guid].LiveObject.QuantityDefault += 1.0m;
            dict[c[5].Guid].LiveObject.LastUpdated = DateTime.Now;
            dict[c[6].Guid].LiveObject.QuantityDefault += 1.0m;
            dict[c[6].Guid].LiveObject.LastUpdated = DateTime.Now;
            dict[c[7].Guid].LiveObject.QuantityDefault += 1.0m;
            dict[c[7].Guid].LiveObject.LastUpdated = DateTime.Now;
            dict[c[8].Guid].LiveObject.QuantityDefault += 1.0m;
            dict[c[8].Guid].LiveObject.LastUpdated = DateTime.Now;
            dict[c[9].Guid].LiveObject.QuantityDefault += 1.0m;
            dict[c[9].Guid].LiveObject.LastUpdated = DateTime.Now;

            results = await Factory<ExampleClass>.SyncDictionary(dict, errorMaker);
            stage++;

            if (!results.Failed.Contains(c[0].Guid) || !dict[c[0].Guid].HasUnconfirmedEdits)
                throw new Exception("Test 0 Failed error handling ");
            if (!results.Failed.Contains(c[1].Guid) || !dict[c[1].Guid].HasUnconfirmedEdits)
                throw new Exception("Test 1 Failed error handling ");

            if (!s.ExampleDB.ContainsKey(c[2].Guid))
                throw new Exception("Test 2 Message did not go");
            if (!s.ExampleDB.ContainsKey(c[3].Guid))
                throw new Exception("Test 3 Message did not go");

            if ((s.ExampleSyncers[c[3].Guid][dict[c[3].Guid].SessionGuid].LiveObject.QuantityDefault - before[3].QuantityDefault) != 1.0m)
                throw new Exception("Test 3 Message qty did not go through");
            if ((s.ExampleSyncers[c[5].Guid][dict[c[5].Guid].SessionGuid].LiveObject.QuantityDefault - before[5].QuantityDefault) != 1.0m)
                throw new Exception("Test 5 Message qty did not go through");

            if ((s.ExampleSyncers[c[6].Guid][dict[c[6].Guid].SessionGuid].LiveObject.QuantityDefault - before[6].QuantityDefault) != 0.0m || !dict[c[6].Guid].HasUnconfirmedEdits )
                throw new Exception("Test 6 Failed message went through");
            if ((s.ExampleSyncers[c[7].Guid][dict[c[7].Guid].SessionGuid].LiveObject.QuantityDefault - before[7].QuantityDefault) != 0.0m || !dict[c[7].Guid].HasUnconfirmedEdits)
                throw new Exception("Test 7 Failed message went through");

            if ((s.ExampleSyncers[c[8].Guid][dict[c[8].Guid].SessionGuid].LiveObject.QuantityDefault - before[8].QuantityDefault) != 1.0m || !dict[c[8].Guid].HasUnconfirmedEdits) // One set went to the server, the other didn't, neither got a response
                throw new Exception("Test 8 Failed message went through");
            if ((s.ExampleSyncers[c[9].Guid][dict[c[9].Guid].SessionGuid].LiveObject.QuantityDefault - before[9].QuantityDefault) != 1.0m || !dict[c[9].Guid].HasUnconfirmedEdits)
                throw new Exception("Test 9 Failed message went through");

            c[7].QuantityDefault += 1.0m;
            dict[c[7].Guid].LiveObject.LastUpdated = DateTime.Now;
            c[9].QuantityDefault += 1.0m;
            dict[c[9].Guid].LiveObject.LastUpdated = DateTime.Now;

            results = await Factory<ExampleClass>.SyncDictionary(dict, errorMaker);
            results = await Factory<ExampleClass>.SyncDictionary(dict, errorMaker);
            results = await Factory<ExampleClass>.SyncDictionary(dict, errorMaker);
            results = await Factory<ExampleClass>.SyncDictionary(dict, errorMaker);
            stage++;
            if ((s.ExampleSyncers[c[6].Guid][dict[c[6].Guid].SessionGuid].LiveObject.QuantityDefault - before[6].QuantityDefault) != 1.0m || dict[c[6].Guid].HasUnconfirmedEdits)
                throw new Exception("Test 6 Qty adjust did not go through");
            if ((s.ExampleSyncers[c[8].Guid][dict[c[8].Guid].SessionGuid].LiveObject.QuantityDefault - before[8].QuantityDefault) != 1.0m || dict[c[8].Guid].HasUnconfirmedEdits)
                throw new Exception("Test 8 Qty adjust did not go through");
            if ((s.ExampleSyncers[c[7].Guid][dict[c[7].Guid].SessionGuid].LiveObject.QuantityDefault - before[7].QuantityDefault) != 2.0m || dict[c[7].Guid].HasUnconfirmedEdits)
                throw new Exception("Test 7 Qty adjust did not go through");
            if ((s.ExampleSyncers[c[9].Guid][dict[c[9].Guid].SessionGuid].LiveObject.QuantityDefault - before[9].QuantityDefault) != 2.0m || dict[c[9].Guid].HasUnconfirmedEdits)
                throw new Exception("Test 9 Qty adjust did not go through");
        }
        /// <summary>
        /// Tests for creation, quantity updates in bulk quantities
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task TestBulkUpdates()
        {
            const int ITEMNO = 500;
            var s = new Server();
            var c = ExampleClass.RandomList(ITEMNO);

            var sumBefore = c.Select(i=>i.TotalQuantity).Sum();
            var countBefore = c.Select(i => i.SubObjects.Count).Sum();
            var sumSubBefore = c.Select(i => i.TotalSubQuantity).Sum();

            var dict = c.ToDictionary(i => i.Guid, i => Factory<ExampleClass>.Create(i.Guid, i, new ExampleClass() { Guid = i.Guid }));
            var di = new System.IO.DirectoryInfo(System.Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            var serverDir = di.CreateSubdirectory("DiffSyncBulkTest");
            if (serverDir.Exists) serverDir.Delete(recursive: true);
            serverDir = di.CreateSubdirectory("DiffSyncBulkTest");

            var clientDir = di.CreateSubdirectory("DiffSyncBulkTestClient");
            if (clientDir.Exists) clientDir.Delete(recursive: true);
            clientDir = di.CreateSubdirectory("DiffSyncBulkTestClient");

            var results = await Factory<ExampleClass>.SyncDictionary(dict, async (p) => s.ReceiveMessage(p), clientDir);
            s.SaveToDisk(serverDir);
            s.LoadFromDisk(serverDir);
            var finalSum = s.ExampleSyncers.SelectMany(i => i.Value).Select(i => i.Value).Select(i=>i.LiveObject.TotalQuantity).Sum();

            {
                if (s.ExampleSyncers.Count != ITEMNO || finalSum != sumBefore)
                    throw new Exception("Bulk import failed; mismatch");
            }

            var r = new Random();
            foreach(var item in dict.Values.OrderBy(i => r.Next()).Take((int)(ITEMNO/5)).ToList())
            {
                var delta = (decimal)((r.NextDouble() - 0.5) * 100.0);
                item.LiveObject.QuantityDefault += delta;
                item.LiveObject.LastUpdated = DateTime.Now;
                sumBefore += delta;
            }

            results = await Factory<ExampleClass>.SyncDictionary(dict, async (p) => s.ReceiveMessage(p), clientDir);
            s.SaveToDisk(serverDir);
            s.LoadFromDisk(serverDir);
            finalSum = s.ExampleSyncers.SelectMany(i => i.Value).Select(i => i.Value).Select(i => i.LiveObject.TotalQuantity).Sum();

            {
                if (s.ExampleSyncers.Count != ITEMNO || finalSum != sumBefore)
                    throw new Exception("Bulk update failed; mismatch");
            }

        }
        /// <summary>
        /// Tests for creation, quantity updates in bulk quantities
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task TestMultipleClientsSimple()
        {
            await TestMultipleClients(makeRandomChanges: true);
        }
        [TestMethod]
        public async Task TestMultipleClientsMedium()
        {
            await TestMultipleClients(0.0, 0.0, 0.0, false, false);
        }
        [TestMethod]
        public async Task TestMultipleClientsHarder()
        {
            await TestMultipleClients(0.5, 0.0, 0.0, false, false);
        }
        [TestMethod]
        public async Task TestMultipleClientsHarderStill()
        {
            await TestMultipleClients(0.5, 0.2, 0.2, false, false);
        }
        [TestMethod]
        public async Task TestMultipleClientsHarderStillWithDropout()
        {
            await TestMultipleClients(0.5, 0.2, 0.2, false, false, false, (clientNo, cycleNo) => cycleNo > 10 && cycleNo < 15);
        }
        [TestMethod]
        public async Task TestMultipleClientsHardAsDiamond()
        {
            await TestMultipleClients(0.5, 0.2, 0.2, false, false, true, (clientNo, cycleNo) => (cycleNo > 10 && cycleNo < 15) || (clientNo == 3 && cycleNo > 20 && cycleNo < 30));
        }

        public async Task TestMultipleClients(double newItemChance = 0.0, double sendPacketLoss = 0.0, double receivePacketLoss = 0.0, bool checkEveryCycle = true, bool syncEveryCycle = true, bool makeRandomChanges = false, Func<int,int,bool> isNetworkDead = null)
        {
            /*
             * how many new items per client on start
             * create new items?
             * make random changes?
             * packet loss %
             * check every cycle?
             */
            // Set up 5 clients with 50 items each, go through 50 cycles, with an occasional modification. By the end all clients and the server should have the same values

            var s = new Server();

            var clients = new List<Client>();
            for (int i = 0; i < 5; i++) clients.Add(new Client(s) { MessageRecvFailRate = receivePacketLoss, MessageSendFailRate = sendPacketLoss });

            var di = new System.IO.DirectoryInfo(System.Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            var rng = new Random();
            const int CYCLECOUNT = 50;
            for( int cycle = 0; cycle < CYCLECOUNT; cycle++)
            {
                var isLastCycle = (cycle == (CYCLECOUNT - 1));

                for (int i = 0; i < 5; i++)
                {
                    var clientCache = di.CreateSubdirectory("DiffSyncCache_" + i.ToString());

                    var c = clients[i];

                    if (cycle == 0 /*&& i == 0*/) clients[i].GenerateItems(50);
                    else if ( rng.NextDouble() < newItemChance && !isLastCycle) clients[i].GenerateItems(1);

                    if (isLastCycle)
                    {
                        c.MessageRecvFailRate = 0.0;
                        c.MessageSendFailRate = 0.0;
                    }
                    else if(isNetworkDead != null && isNetworkDead(i, cycle))
                    {
                        c.MessageRecvFailRate = 1.0;
                        c.MessageSendFailRate = 1.0;
                    }
                    else
                    {
                        c.MessageRecvFailRate = receivePacketLoss;
                        c.MessageSendFailRate = sendPacketLoss;
                    }
                    await c.Cycle(rng, clientCache, /* i > 0 */ !isLastCycle, makeRandomChanges); // Don't make changes on the last cycle
                }

                if(syncEveryCycle || isLastCycle )
                {
                    // It may happen that in the second last cycle a new item is created but the packet is dropped, so it may take two error free cycles to fix:
                    for (int i = 0; i < 5; i++)
                    {
                        var clientCache = di.CreateSubdirectory("DiffSyncCache_" + i.ToString());
                        var c = clients[i];
                        await c.Cycle(rng, clientCache, false);
                    }
                }

                {
                    var serverLatestItems = s.ExampleDB.Values.Select(i => i.OrderByDescending(v => v.Key).First()).ToDictionary(i => i.Value.Guid, i => i.Value);
                    var serverTotal = serverLatestItems.Select(i => i.Value.TotalQuantity).Sum();
                    var serverCount = s.ExampleDB.Values.Count();

                    var clientRunningSum = clients.Select(c => c.RunningTotalQuantity).Sum();

                    var clientItems = new Dictionary<int, Dictionary<Guid, (bool, ExampleClass)>>();
                    for (int i = 0; i < 5; i++)
                    {
                        var c = clients[i];
                        
                        var syncerItems = c.Syncers.ToDictionary(kvp => kvp.Key, kvp => (true, kvp.Value.LiveObject));
                        var storeItems = c.LocalStore.ToDictionary(kvp => kvp.Key, kvp => (false, kvp.Value));
                        var storeOnlyGuids = storeItems.Keys.Except(syncerItems.Keys);
                        var res = syncerItems.Union(storeItems.Where(kvp => storeOnlyGuids.Contains(kvp.Key))).ToDictionary(k => k.Key, k => k.Value);
                        clientItems.Add(i, res);
                    }
                    for (int i = 0; i < 5; i++)
                    {
                        var res = clientItems[i];

                        var clientTotal = res.Values.Select(r => r.Item2.TotalQuantity).Sum();
                        var clientCount = res.Values.Select(r => r).Count();

                        var differences = (from server in serverLatestItems
                                           from client in res
                                           where server.Key == client.Key && server.Value.TotalQuantity != client.Value.Item2.TotalQuantity
                                           select (server, client)).ToList();
                        var clientValues = new Dictionary<Guid, Dictionary<int, (bool, ExampleClass)>>();
                        for (int j = 0; j < 5; j++)
                        {
                            foreach (var d in differences)
                            {
                                if (!clientValues.ContainsKey(d.client.Key)) clientValues.Add(d.client.Key, new Dictionary<int, (bool, ExampleClass)>());
                                if(clientItems[j].ContainsKey(d.client.Key)) clientValues[d.client.Key].Add(j, clientItems[j][d.client.Key]);
                            }
                        }

                        if (serverCount != clientCount)
                        {

                            if (checkEveryCycle || isLastCycle)
                                throw new Exception("Server count and client count mismatch");
                            else
                                Console.WriteLine("Warning: Server count and client count mismatch");
                        }

                        var diff = serverTotal - clientTotal;
                        if (serverTotal != clientTotal)
                        {
                            //int a = 0;
                            //a++;
                            //var clientCache = di.CreateSubdirectory("DiffSyncCache_" + i.ToString());
                            //await clients[i].Cycle(rng, clientCache, false);
                            if (checkEveryCycle || isLastCycle)
                                throw new Exception("Server total and client total mismatch");
                            else
                                Console.WriteLine("Warning: Server total and client total mismatch");
                        }
                        else if ( serverTotal != clientRunningSum) 
                        {
                            // Errors in the client running sum but not the client total indicates that a change has been lost or two made the same change at the same time, but that everything synced up right.
                            /*
                             * if (checkEveryCycle || isLastCycle)
                                throw new Exception("Server total and client total mismatch");
                            else
                                Console.WriteLine("Warning: Server total and client total mismatch");
                                */
                        }
                    }
                }
            }
        }
    }

    public class Server
    {
        public Dictionary<int, ExampleClass> ExampleDBByRevision = new Dictionary<int, ExampleClass>();
        private int NextExampleDBRevisionID = 1;
        public Dictionary<Guid, Dictionary<int, ExampleClass>> ExampleDB = new Dictionary<Guid, Dictionary<int,ExampleClass>>();
        public Dictionary<Guid, Dictionary<int, ExampleClass.SubClass>> ExampleSubDB = new Dictionary<Guid, Dictionary<int, ExampleClass.SubClass>>();

        public Dictionary<Guid, Dictionary<Guid, Factory<ExampleClass>.Syncer>> ExampleSyncers = new Dictionary<Guid, Dictionary<Guid, Factory<ExampleClass>.Syncer>>();
        public Dictionary<Guid, Dictionary<Guid, Factory<ExampleClass.SubClass>.Syncer>> ExampleSubSyncers = new Dictionary<Guid, Dictionary<Guid, Factory<ExampleClass.SubClass>.Syncer>>();

        public void LoadFromDisk(DirectoryInfo di)
        {
            di = di.CreateSubdirectory("DiffSync_TestData");
            var exampleDir = di.CreateSubdirectory("ExampleSyncers");
            var exampleSubDir = di.CreateSubdirectory("ExampleSubSyncers");
            ExampleSyncers = Factory<ExampleClass>.LoadServerDictionary(exampleDir);
            ExampleSubSyncers = Factory<ExampleClass.SubClass>.LoadServerDictionary(exampleSubDir);

        }
        public void SaveToDisk(DirectoryInfo di)
        {
            di = di.CreateSubdirectory("DiffSync_TestData");
            var exampleDir = di.CreateSubdirectory("ExampleSyncers");
            var exampleSubDir = di.CreateSubdirectory("ExampleSubSyncers");
            foreach (var kvp in ExampleSyncers)
            {
                var objGuid = kvp.Key;
                foreach (var subKVP in kvp.Value)
                {
                    var sessGuid = subKVP.Key;
                    var str = subKVP.Value.Serialize();
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(exampleDir.FullName, objGuid.ToString() + sessGuid.ToString() + ".bin"), str);
                }
            }
            foreach (var kvp in ExampleSubSyncers)
            {
                var objGuid = kvp.Key;
                foreach (var subKVP in kvp.Value)
                {
                    var sessGuid = subKVP.Key;
                    var str = subKVP.Value.Serialize();
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(exampleSubDir.FullName, objGuid.ToString() + sessGuid.ToString() + ".bin"), str);
                }
            }
        }
        public List<ExampleClass> GetJournalSince(int lastID)
        {
            return ExampleDBByRevision.Where(kvp => kvp.Key > lastID).Select(kvp => kvp.Value).ToList();
        }
        public Factory<ExampleClass>.MessagePacket ReceiveMessage(Factory<ExampleClass>.MessagePacket clientMsg)
        {
            if (!ExampleSyncers.ContainsKey(clientMsg.ObjectGuid)) ExampleSyncers.Add(clientMsg.ObjectGuid, new Dictionary<Guid, Factory<ExampleClass>.Syncer>());
            
            ExampleClass live, shadow = new ExampleClass() { Guid = clientMsg.ObjectGuid };
            if (ExampleDB.ContainsKey(clientMsg.ObjectGuid))
            {
                var itemRevs = ExampleDB[clientMsg.ObjectGuid];
                var maxRev = itemRevs.Keys.Max();
                var latestRev = itemRevs[maxRev];

                // Revision = 0 means it's the first upload, yet we have the item. This can happen if a message gets lost; the client doesn't know the server got the message
                if( clientMsg.Revision != 0 )
                {
                    if( !ExampleDBByRevision.ContainsKey(clientMsg.Revision))
                        return new Factory<ExampleClass>.MessagePacket(clientMsg.SessionGuid, clientMsg.ObjectGuid, null) { ClientCompleted = true, ServerError = new Exception("Could not find the given revision #") };

                    // We don't need to start from an existing shadow if the client's base version is empty (and it is likely we have a session with a good shadow already)
                    var baseRev = ExampleDBByRevision[clientMsg.Revision]; // Very important to use this as the common start point
                    shadow = baseRev;
                }

                live = latestRev;
                
            }
            else
            {
                // No syncer, no item; new item
                live = new ExampleClass() { Guid = clientMsg.ObjectGuid };
            }

            if (!ExampleSyncers[clientMsg.ObjectGuid].ContainsKey(clientMsg.SessionGuid))
            {
                // No syncer
                if (clientMsg.Message.SenderPeerVersion != 0) // This could be because of a very old syncer that we discarded? May need to account for obsolete syncers here.
                    return new Factory<ExampleClass>.MessagePacket(clientMsg.SessionGuid, clientMsg.ObjectGuid, null) { ClientCompleted = true, ServerError = new Exception("The SenderPeerVersion for a new syncer is not 0") };

                var syncer = Factory<ExampleClass>.Create(clientMsg.ObjectGuid, live.Clone(), shadow.Clone());
                syncer.ObjectGuid = clientMsg.ObjectGuid;
                syncer.SessionGuid = clientMsg.SessionGuid;
                ExampleSyncers[clientMsg.ObjectGuid].Add(clientMsg.SessionGuid, syncer);
            }

            {
                // Syncer exists (now)
                var syncer = ExampleSyncers[clientMsg.ObjectGuid][clientMsg.SessionGuid];
                
                syncer.Live.StateObject.SetStateData(live.Clone());

                var changed = syncer.ReadMessageCycle(clientMsg.Message);
                if (changed)
                {
                    syncer.LiveObject.Revision = NextExampleDBRevisionID++;
                    var newRec = syncer.Live.StateObject.GetStateData();

                    if (!ExampleDB.ContainsKey(clientMsg.ObjectGuid))
                        ExampleDB.Add(clientMsg.ObjectGuid, new Dictionary<int, ExampleClass>());

                    if (ExampleDB[clientMsg.ObjectGuid].ContainsKey(newRec.Revision))
                        throw new Exception("Database already contains revision being entered.");

                    if (ExampleDBByRevision.ContainsKey(newRec.Revision))
                        throw new Exception("Database already contains revision being entered.");

                    ExampleDB[clientMsg.ObjectGuid].Add(newRec.Revision, newRec);
                    ExampleDBByRevision.Add(newRec.Revision, newRec);
                }
                var msg = syncer.MakeMessageCycle(clientMsg.Message);
                return new Factory<ExampleClass>.MessagePacket(clientMsg.SessionGuid, clientMsg.ObjectGuid, msg);
            }
        }
    }

    public class Client
    {
        public Server Server;
        public Dictionary<Guid, ExampleClass> LocalStore = new Dictionary<Guid, ExampleClass>();
        public Dictionary<Guid, Factory<ExampleClass>.Syncer> Syncers = new Dictionary<Guid, Factory<ExampleClass>.Syncer>();
        public double MessageSendFailRate = 0.0;
        public double MessageRecvFailRate = 0.0;
        public decimal RunningTotalQuantity = 0.0m;
        public int AlterItems = 0;
        public Client(Server _server)
        {
            Server = _server;
        }
        public void GenerateItems(int itemNo)
        {
            for(int i = 0; i < itemNo; i++ )
            {
                var c = ExampleClass.Random();
                LocalStore.Add(c.Guid, c);
                Syncers.Add(c.Guid, Factory<ExampleClass>.Create(c.Guid, c, new ExampleClass() { Guid = c.Guid }));
                RunningTotalQuantity += c.TotalQuantity;
            }
        }
        int LastVersionID = 0;
        public int FetchAllUpdatedItems()
        {
            var newItems = Server.ExampleDBByRevision.Where(i => i.Value.Revision > LastVersionID).Select(i=> UnitTest1.CloneItem(i.Value)).ToList();
            if (newItems.Count == 0) return LastVersionID;

            var prevID = LastVersionID;
            LastVersionID = newItems.Select(i => i.Revision).Max();

            foreach(var i in newItems)
            {
                if (!LocalStore.ContainsKey(i.Guid))
                    LocalStore.Add(i.Guid, i);
                else
                    LocalStore[i.Guid] = i;

                if ( Syncers.ContainsKey(i.Guid))
                    Syncers[i.Guid].ServerCheckCopy = i;
            }
            return LastVersionID - prevID;
        }
        public enum QuantityTypes
        {
            Default,
            ClientFirst,
            Important,
            NotImportant,
            ServerFirst,
            LatestFirst
        }
        public List<ExampleClass> AlterQuantities(List<Guid> items, QuantityTypes type = QuantityTypes.Default, bool makeRandomChange = false)
        {
            var alteredItems = new List<ExampleClass>();

            var r = new Random();
            foreach (var c in items)
            {
                ExampleClass item;
                if (Syncers.ContainsKey(c))
                    item = Syncers[c].LiveObject;
                else
                    item = LocalStore[c].Clone();

                var delta = makeRandomChange ? 1.0m : (decimal)((r.NextDouble() - 0.5) * 100.0);
                switch(type)
                {
                    case QuantityTypes.ClientFirst: item.QuantityClientFirst+= delta;      break;
                    //case QuantityTypes.Important: item.QuantityImportant += delta;       break;
                    case QuantityTypes.LatestFirst: item.QuantityLatestFirst += delta;     break;
                    //case QuantityTypes.NotImportant: item.QuantityNotImportant += delta;    break;
                    //case QuantityTypes.ServerFirst: item.QuantityServerFirst += delta;     break;
                    default: item.QuantityDefault += delta;         break;
                }

                alteredItems.Add(item);

                RunningTotalQuantity += delta;
            }
            return alteredItems;
        }
        private CancellationTokenSource Cancel = new CancellationTokenSource();
        public void Stop()
        {
            Cancel.Cancel();
        }
        public async Task Cycle(Random rng, DirectoryInfo cacheDir, bool makeChange = false, bool makeRandomChange = false)
        {

            var newItems = FetchAllUpdatedItems();
            // If we are making a change make 5 changes:
            var alteredItems = AlterQuantities(LocalStore.Keys.Union(Syncers.Keys).Distinct().OrderBy(i => rng.NextDouble()).Take(makeChange ? 5 : 0).ToList(), QuantityTypes.Default, makeRandomChange);
            foreach(var item in alteredItems)
            {
                item.LastUpdated = DateTime.Now;

                if (Syncers.ContainsKey(item.Guid))
                {
                    // Nothing to do, this will get picked up in SyncDictionary
                }
                else
                {
                    // Need to make a new syncer for this item:
                    Syncers.Add(item.Guid, Factory<ExampleClass>.Create(item.Guid, item));
                }
            }

            var syncResults = await Factory<ExampleClass>.SyncDictionary(Syncers, async (m) =>
            {
                if (rng.NextDouble() < MessageSendFailRate) return null;

                var retMsg = Server.ReceiveMessage(m);

                if (rng.NextDouble() < MessageRecvFailRate) return null;

                return retMsg;
            }, cacheDir);
        }
        public Task Loop()
        {
            var t = new Task(async () =>
            {
                

                var di = new System.IO.DirectoryInfo(System.Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
                var testDir = di.CreateSubdirectory("DiffSyncTest");
                if (testDir.Exists) testDir.Delete(recursive: true);
                testDir = di.CreateSubdirectory("DiffSyncTest");

                GenerateItems(1000);

                var rng = new Random();
                while (!Cancel.Token.IsCancellationRequested)
                {
                    await Cycle(rng, testDir);
                    await Task.Delay(1007);
                }
            }, Cancel.Token);
            t.Start();
            return t;
        }
    }
    [DataContract]
    public class ExampleClass : DiffSync.NET.Reflection.IReflectionSyncable<ExampleClass>
    {
        public override string ToString() => "ExampleClass, Total=" + TotalQuantity.ToString();
        public static ExampleClass Random() {
            var e = new ExampleClass();

            var r = new Random();
            e.Guid = Guid.NewGuid();
            e.QuantityDefault = (decimal)(1000.0 * r.NextDouble());
            //e.QuantityServerFirst = (decimal)(1000.0 * r.NextDouble());
            e.QuantityClientFirst = (decimal)(1000.0 * r.NextDouble());
            e.QuantityLatestFirst = (decimal)(1000.0 * r.NextDouble());
            //e.QuantityNotImportant = (decimal)(1000.0 * r.NextDouble());
            //e.QuantityImportant = (decimal)(1000.0 * r.NextDouble());
            e.LastUpdated = DateTime.Now;
            e.QuantityDefault = e.QuantityClientFirst =  e.QuantityLatestFirst =  1.0m;
            for (int i = 0; i < r.Next(5,15); i++ )
                e.SubObjects.Add(new SubClass() { ParentGuid = e.Guid, Guid = Guid.NewGuid(), Quantity = (decimal)(1000.0 * r.NextDouble()) });

            return e;
        }
        public static List<ExampleClass> RandomList(int count)
        {
            var r = new List<ExampleClass>();
            for(int i = 0; i < count; i++) r.Add(Random());
            return r;
        }
        [DataMember, DiffSync]
        public Guid Guid { get; set; } = new Guid();
        [DataMember, DiffSync, DiffSyncPriorityToServer]
        public int Revision { get; set; }
        [DataMember, DiffSync, DiffSyncPriorityToLatestChange]
        public decimal QuantityDefault { get; set; }
        //[DataMember, DiffSync, DiffSyncPriorityToServer]
        //public decimal QuantityServerFirst { get; set; }
        [DataMember, DiffSync, DiffSyncPriorityToClient]
        public decimal QuantityClientFirst { get; set; }
        [DataMember, DiffSync, DiffSyncPriorityToLatestChange]
        public decimal QuantityLatestFirst { get; set; }
        //[DataMember, DiffSync, DiffSyncNotImportant]
        //public decimal QuantityNotImportant { get; set; }
        //[DataMember, DiffSync, DiffSyncImportant]
        //public decimal QuantityImportant { get; set; }
        [DataMember, DiffSync, DiffSyncPriorityToLatestChange]
        public DateTime LastUpdated { get; internal set; }
        public decimal TotalQuantity => QuantityDefault + QuantityClientFirst + QuantityLatestFirst;
        public List<SubClass> SubObjects { get; set; } = new List<SubClass>();
        public decimal TotalSubQuantity => SubObjects.Select(o => o.Quantity).Sum();

        [DataContract]
        public class SubClass : DiffSync.NET.Reflection.IReflectionSyncable<SubClass>
        {
            [DataMember, DiffSync]
            public Guid ParentGuid;
            [DataMember, DiffSync]
            public Guid Guid;
            [DataMember, DiffSync, DiffSyncPriorityToServer]
            public int Revision { get; set; }
            [DataMember, DiffSync, DiffSyncPriorityToLatestChange]
            public decimal Quantity;
            [DataMember, DiffSync, DiffSyncPriorityToLatestChange]
            public DateTime LastUpdated { get; internal set; }
            object IReflectionSyncable<SubClass>.CopyStateFromLock { get; } = new object();

            public List<Point> DiffSyncRemovedStrokes => throw new NotImplementedException();

            SubClass IReflectionSyncable<SubClass>.CopyStateFrom(SubClass copyFromObj)
            {
                lock (((IReflectionSyncable<SubClass>)this).CopyStateFromLock)
                {
                    lock (((IReflectionSyncable<SubClass>)copyFromObj).CopyStateFromLock)
                    {
                        Guid = copyFromObj.Guid;
                        Revision = copyFromObj.Revision;
                        Quantity = copyFromObj.Quantity;
                    }
                }
                return this;
            }
        }
        object IReflectionSyncable<ExampleClass>.CopyStateFromLock { get; } = new object();

        public List<Point> DiffSyncRemovedStrokes => throw new NotImplementedException();

        ExampleClass IReflectionSyncable<ExampleClass>.CopyStateFrom(ExampleClass copyFromObj)
        {
            lock(((IReflectionSyncable<ExampleClass>)this).CopyStateFromLock)
            {
                lock (((IReflectionSyncable<ExampleClass>)copyFromObj).CopyStateFromLock)
                {
                    Revision = copyFromObj.Revision;
                    Guid = copyFromObj.Guid;
                    QuantityDefault = copyFromObj.QuantityDefault;
                    //QuantityServerFirst = copyFromObj.QuantityServerFirst;
                    QuantityClientFirst = copyFromObj.QuantityClientFirst;
                    QuantityLatestFirst = copyFromObj.QuantityLatestFirst;
                    //QuantityNotImportant = copyFromObj.QuantityNotImportant;
                    //QuantityImportant = copyFromObj.QuantityImportant;
                    LastUpdated = copyFromObj.LastUpdated;
                }
            }
            return this;
        }
        public ExampleClass Clone()
        {
            var n = new ExampleClass();
            var newItem = ((IReflectionSyncable<ExampleClass>)n);
            newItem.CopyStateFrom(this);
            return n;
        }
    }
}
