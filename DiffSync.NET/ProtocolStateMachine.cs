using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/*
    DiffSync.NET - A Differential Synchronization library for .NET
    Copyright (C) 2019 Kestas J. Kuliukas

    This library is free software; you can redistribute it and/or
    modify it under the terms of the GNU Lesser General Public
    License as published by the Free Software Foundation; either
    version 2.1 of the License, or (at your option) any later version.

    This library is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
    Lesser General Public License for more details.

    You should have received a copy of the GNU Lesser General Public
    License along with this library; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301
    USA
    */

namespace DiffSync.NET
{
    public class ProtocolStateMachine<T,P,D,S> where T : class, IDiffSyncable, new() where D : Diff where P : Patch where S : StateDataDictionary
    {
        public void Start(T o )
        {
            Live = new LiveState<T,D,S>(o);
            Shadow = new ShadowState<T,D,S>(o.Clone() as T);
            BackupShadow = new BackupShadowState<T, D, S>(o.Clone() as T);
        }
        /// <summary>
        /// An unique numerical identifier for each message going out.
        /// </summary>
        public int SeqNum = 1;
        /// <summary>
        /// The sequence number of the response we are waiting for, or null if not waiting for a response
        /// </summary>
        public int? WaitingSeqNum = null;
        //public Action<string> DiagNotify, DiagWarning, DiagError;
        public Action<StateDataDictionary> LogState { get; set; }
        public LiveState<T, D, S> Live { get; private set; }
        public ShadowState<T, D, S> Shadow { get; private set; }
        public BackupShadowState<T, D, S> BackupShadow { get; private set; }

        public DiffQueue<D> UnconfirmedEdits = new DiffQueue<D>();
        public List<int> ReceivedEdits = new List<int>();

        public bool IsMessageInSequence(EditsMessage<D> edits)
        {
            if (edits.IsResponse && edits.RequestSeqNum != WaitingSeqNum)
                return false; // This indicates this is a response but the sequence number isn't right; this must be an old message
            else if (!edits.IsResponse && WaitingSeqNum != null)
                return false; // This indicates we are receiving a request from our peer even though we have sent a message without a response; we need our response first.
            else
            {
                return true;
            }
        }
        //public EditsMessage LatestEditsReceived;
        public void OnReceivedEdits(EditsMessage<D> edits)
        {
            //LatestEditsReceived = edits;
            edits.Diffs.RemoveAll(v => v.Version < Shadow.PeerVersion);
            UnconfirmedEdits.Diffs.RemoveAll(d => d.Version <= edits.SenderPeerVersion);
        }
        public void CheckAndPerformBackupRevert(EditsMessage<D> LatestEditsReceived)
        {
            if (LatestEditsReceived == null || LatestEditsReceived.Diffs.Where(v=>v.Version >= Shadow.PeerVersion).Count() == 0) return;

                // 4 : Check whether our shadow versions are out of sync and we need to rebase to the version in BackupShadow
            if (Shadow.PeerVersion != LatestEditsReceived.Diffs.Select(d=>d.Version).Min() || Shadow.Version != LatestEditsReceived.SenderPeerVersion) // Only do this if the message has edits in, otherwise this could be a missed message that can still be processed as is in historic order without clearing and rediffing
            {
                // Something is wrong e.g. missing packet. Return shadow to backup shadow which should be synced on both sides, and we can issue a new sync against that.
                Shadow = new ShadowState<T, D, S>(BackupShadow);
                Live.Version = Shadow.Version;
                UnconfirmedEdits = new DiffQueue<D>();
                //Live.Version = Shadow.Version;
            }
        }
        public void ProcessEditsToShadow(EditsMessage<D> LatestEditsReceived)
        {
            if (LatestEditsReceived == null) return;

            // Either backupshadow or shadow should match the peer's version given 
            foreach (var edit in LatestEditsReceived.Get().OrderBy(e => e.Version))
            {
                if (edit.Version != Shadow.PeerVersion) continue;
                try
                {
                    // Apply these changes to the shadow 
                    Shadow.Apply(new Patch(edit));
                    Shadow.PeerVersion++;


                    ReceivedEdits.Add(edit.Version);
                }
                catch (Exception ex)
                {
                    throw ex;
                }
            }
        }
        public void TakeBackupIfApplicable(EditsMessage<D> LatestEditsReceived)
        {
            if (LatestEditsReceived == null) return;//|| LatestEditsReceived.Diffs.Count==0) return;

            if (Shadow.Version == LatestEditsReceived.SenderPeerVersion)// Shadow.PeerVersion == (LatestEditsReceived.Diffs.Select(f=>f.Version).Max()+1))
            {
                BackupShadow = new BackupShadowState<T, D, S>(Shadow);
            }
        }
        public void ProcessLocal()
        {
            var liveDiff = Live.PollForLocalDifferencesOrNull();

            if( liveDiff != null && liveDiff.GetData.Count > 0 )
            {
                Live.Apply(new Patch(liveDiff));
            }

            // 1 a & b : Take Live client vs Shadow (last server sync) difference as a diff, which gives local updates relative to server shadow
            var diff = Live.DiffAgainst(Shadow);

            if (diff != null)
            {
                //Live.Version = diff.Version;

                Live.Version++;

                LogState(Live.QuickLockAndCopy());

                // 2 : Save the diff in the unconfirmed stack edit to be sent to our peer
                UnconfirmedEdits.Add(diff);

                // 3 : Apply the diff from live to our shadow, now that we have sent out the edit that will bring our peer up to date.
                Shadow.Apply(new Patch(diff));
                Shadow.Version = Live.Version;

                // Live.Apply(new Patch(diff));
            }
        }
        public void ProcessEditsToLive(EditsMessage<D> LatestEditsReceived)
        {
            if (LatestEditsReceived==null) return;
            foreach (var edit in LatestEditsReceived.Get().OrderBy(e => e.Version))
            {
                Live.Apply(new Patch(edit));
            }
        }
        public bool IsWaitingForMessage => WaitingSeqNum != null;
        public EditsMessage<D> GenerateMessage(EditsMessage<D> LatestEditsReceived)
        {
            int requestSequenceNum;
            bool isResponse = false;
            if (LatestEditsReceived?.IsResponse ?? true)
            {
                // If it's a response or there's no message then increment the sequence number
                requestSequenceNum = ++SeqNum;
                WaitingSeqNum = requestSequenceNum;
                // The incoming message should be IsResponse==true i.e. a response , or else null and this is a new request
            }
            else
            {
                isResponse = true;
                WaitingSeqNum = null;
                requestSequenceNum = LatestEditsReceived.RequestSeqNum;
            }

            
            if ((UnconfirmedEdits?.Diffs?.Count ?? 0) == 0) return new EditsMessage<D>(Shadow.PeerVersion, requestSeqNum: requestSequenceNum, isResponse: !(LatestEditsReceived?.IsResponse ?? true));

            var em = new EditsMessage<D>(Shadow.PeerVersion, requestSequenceNum, !(LatestEditsReceived?.IsResponse ?? true));
            foreach (var e in UnconfirmedEdits.Get()) em.Add(e);
            //em.ReceivedEdits = ReceivedEdits;
            return em;
        }
        /// <summary>
        /// Cycle through a whole receive & send cycle.
        /// </summary>
        /// <param name="em"></param>
        /// <returns></returns>
        public EditsMessage<D> Cycle(EditsMessage<D> em)
        {
            ProcessEditsToShadow(em);
            CheckAndPerformBackupRevert(em);
            ProcessEditsToLive(em);
            TakeBackupIfApplicable(em);
            ProcessLocal();
            return GenerateMessage(em);
        }
    }
}
