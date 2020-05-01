using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
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
    [DataContract]
    public class ProtocolStateMachine<PATCHER, D, S> where PATCHER : class, IDiffSyncable<S, D>, new() where D : class, IDiff where S : class
    {
        /// <summary>
        /// An unique numerical identifier for each message going out.
        /// </summary>
        [DataMember]
        public int NextSequenceNo = 0;
        /// <summary>
        /// The sequence number of the response we are waiting for, or null if not waiting for a response
        /// </summary>
        [DataMember]
        public int? WaitingSeqNum = null;
        [DataMember]
        public LiveState<PATCHER, D, S> Live { get; private set; }
        [DataMember]
        public ShadowState<PATCHER, D, S> Shadow { get; private set; }
        [DataMember]
        public BackupShadowState<PATCHER, D, S> BackupShadow { get; private set; }
        [DataMember]
        protected DiffQueue<D> UnconfirmedEdits = new DiffQueue<D>();
        public void ClearUnconfirmedEdits() => UnconfirmedEdits.Diffs.Clear();
        /// <summary>
        /// Initialize with a shadow that isn't the same as live. Useful when starting a session where
        /// we may not be able to tell the peer what the initial state is, but the server can initialize
        /// with a default state.
        /// </summary>
        /// <param name="o"></param>
        /// <param name="shadow"></param>
        public void Initialize(PATCHER live, PATCHER shadow, PATCHER backupshadow)
        {
            Live = new LiveState<PATCHER, D, S>(live);
            Shadow = new ShadowState<PATCHER, D, S>(shadow);
            BackupShadow = new BackupShadowState<PATCHER, D, S>(backupshadow);
        }
        private bool IsMessageInSequence(Message<D> edits)
        {
            if (edits.IsResponse && edits.RequestSeqNum != WaitingSeqNum)
                return false; // This indicates this is a response but the sequence number isn't right; this must be an old message
            else if (!edits.IsResponse && WaitingSeqNum != null)
                return false; // This indicates we are receiving a request from our peer even though we have sent a message without a response; we need our response first.
            else
                return true;
        }

        public bool TryReceiveEdits(Message<D> edits)
        {
            if (!IsMessageInSequence(edits)) return false;

            if( WaitingSeqNum != null && WaitingSeqNum == edits.RequestSeqNum )
                WaitingSeqNum = null;

            //edits.Diffs.RemoveAll(v => v.Version < BackupShadow.PeerVersion);
            UnconfirmedEdits.Diffs.RemoveAll(d => d.Version < edits.SenderPeerVersion); // Using the BackupShadow.PeerVersion, which I would have thought 

            return true;
        }
        public void CheckAndPerformBackupRevert(Message<D> LatestEditsReceived)
        {
            if (LatestEditsReceived == null || LatestEditsReceived.Diffs.Where(v=>v.Version >= Shadow.PeerVersion).Count() == 0) return;

                // 4 : Check whether our shadow versions are out of sync and we need to rebase to the version in BackupShadow
            if (Shadow.PeerVersion != LatestEditsReceived.Diffs.Select(d=>d.Version).Min() || Shadow.Version != LatestEditsReceived.SenderPeerVersion) // Only do this if the message has edits in, otherwise this could be a missed message that can still be processed as is in historic order without clearing and rediffing
            {
                if( BackupShadow.PeerVersion == Shadow.PeerVersion && BackupShadow.Version == Shadow.Version )
                {
                    throw new Exception("Attempting to revert to backup, but already reverted; DiffSyncer is unrecoverable");
                }
                // Something is wrong e.g. missing packet. Return shadow to backup shadow which should be synced on both sides, and we can issue a new sync against that.
                Shadow = new ShadowState<PATCHER, D, S>(BackupShadow.StateObject);
                Shadow.PeerVersion = BackupShadow.PeerVersion;
                Shadow.Version = BackupShadow.Version;

                Live.Version = Shadow.Version;
                UnconfirmedEdits.Diffs.Clear();
                //Live.Version = Shadow.Version;
            }
        }
        public List<D> ProcessEditsToShadow(Message<D> LatestEditsReceived)
        {
            var editsApplied = new List<D>();

            if (LatestEditsReceived == null) return editsApplied;

            // Either backupshadow or shadow should match the peer's version given 
            foreach (var edit in LatestEditsReceived.Get().OrderBy(e => e.Version))
            {
                if (edit.Version != Shadow.PeerVersion) continue;

                // Apply these changes to the shadow 
                Shadow.Apply(edit, LatestEditsReceived.IsResponse, true);
                Shadow.PeerVersion++;

                editsApplied.Add(edit);
            }

            return editsApplied;
        }
        public void TakeBackupIfApplicable(Message<D> LatestEditsReceived)
        {
            if (LatestEditsReceived == null) return;//|| LatestEditsReceived.Diffs.Count==0) return;

            if (Shadow.Version == LatestEditsReceived.SenderPeerVersion)// Shadow.PeerVersion == (LatestEditsReceived.Diffs.Select(f=>f.Version).Max()+1))
            {
                BackupShadow = new BackupShadowState<PATCHER, D, S>(Shadow.StateObject);
                BackupShadow.PeerVersion = Shadow.PeerVersion;
                BackupShadow.Version = Shadow.Version;
            }
        }
        // Live doesn't need to be diffed; just diff against the server shadow and that will show what has changed
        //public D DiffApplyLive()
        //{
        //    var liveDiff = Live.PollForLocalDifferencesOrNull();

        //    if (liveDiff != null)
        //    {
        //        Live.Apply(liveDiff, null, false);
        //        // Save the time these local modifications where made to which field, and add to the timestamp dictionary,
        //        // so that we can ensure we always send if we have the latest, or receive the latest if we don't:
        //    }

        //    return liveDiff;
        //}
        public D DiffApplyShadow(DateTime newDiffTimestamp)
        {
            Debug.WriteLine("DiffApplyShadow");
            // 1 a & b : Take Live client vs Shadow (last server sync) difference as a diff, which gives local updates relative to server shadow
            var diff = Live.DiffAgainst(Shadow, newDiffTimestamp);

            if (diff != null)
            {

                Live.Version++;
                Debug.WriteLine("Live version =" + Live.Version);

                // 2 : Save the diff in the unconfirmed stack edit to be sent to our peer
                UnconfirmedEdits.Add(diff);

                // 3 : Apply the diff from live to our shadow, now that we have sent out the edit that will bring our peer up to date.
                Shadow.Apply(diff, null, true);
                Shadow.Version = Live.Version;
            }
            return diff;
        }
        public List<D> ProcessEditsToLive(List<D> editsToApply, bool isResponse)
        {
            Debug.WriteLine("ProcessEditsToLive");
            var editsApplied = new List<D>();

            if (editsToApply == null) return editsApplied;
            foreach (var edit in editsToApply)
            {
                Live.Apply(edit, isResponse, false);
            }

            return editsApplied;
        }
        public Message<D> GenerateMessage(Message<D> LatestEditsReceived)
        {
            Debug.WriteLine("GenerateMessage");
            int requestSequenceNum;
            bool isResponse = false;
            if (LatestEditsReceived?.IsResponse ?? true)
            {
                // If it's a response or there's no message then increment the sequence number
                requestSequenceNum = NextSequenceNo++;
                WaitingSeqNum = requestSequenceNum;
                // The incoming message should be IsResponse==true i.e. a response , or else null and this is a new request
            }
            else
            {
                isResponse = true;
                requestSequenceNum = LatestEditsReceived.RequestSeqNum;
            }

            if ((UnconfirmedEdits?.Diffs?.Count ?? 0) == 0) return new Message<D>(Shadow.PeerVersion, requestSeqNum: requestSequenceNum, isResponse: isResponse);

            var em = new Message<D>(Shadow.PeerVersion, requestSequenceNum, isResponse);
            foreach (var e in UnconfirmedEdits.Get()) em.Add(e);
            return em;
        }


        public bool IsWaitingForMessage => WaitingSeqNum != null;
        public bool HasUnconfirmedEdits => UnconfirmedEdits.Diffs.Count > 0;

        // Returns true if the peer version has changed i.e. we have received an update from this message. After this message the live copy should be updated.
        public bool ReadMessageCycle(Message<D> em)
        {
            if(!TryReceiveEdits(em)) return false;

            var prevVersion = Shadow.PeerVersion;
            var appliedEdits = ProcessEditsToShadow(em);
            CheckAndPerformBackupRevert(em);
            ProcessEditsToLive(appliedEdits, em.IsResponse);
            TakeBackupIfApplicable(em);
            return (prevVersion != Shadow.PeerVersion);
        }
        /// <summary>
        /// Live should be updated prior to calling this. The last message received is needed so that we know whether this is a response message or not
        /// </summary>
        /// <returns></returns>
        public Message<D> MakeMessageCycle(Message<D> em, DateTime newDiffTimestamp)
        {
            DiffApplyShadow(newDiffTimestamp);
            return GenerateMessage(em);
        }
    }
}
