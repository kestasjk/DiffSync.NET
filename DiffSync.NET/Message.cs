using System;
using System.Collections.Generic;
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
    /// <summary>
    /// The only message that is sent in this system; containing the 
    /// version the sender's shadow peer is at (i.e. which patches have
    /// we successfully merged), 
    /// 
    /// plus a flag saying whether it is a request or response to a request.
    /// There are only two stages; a message is sent with IsResponse=false , 
    /// and this triggers a response back with IsResponse=true set, and with
    /// the same RequestSeqNum that was sent. This is used to ensure that 
    /// any response they get is the response to the particular message they 
    /// sent. Any message before or after due to ordering issues are no good.
    /// 
    /// Finally has a list of Diff objects
    /// 
    /// </summary>
    [DataContract]
    public class Message<D> : DiffQueue<D> where D : IDiff
    {
        public Message(int senderPeerVersion, int requestSeqNum, bool isResponse) : base()
        {
            SenderPeerVersion = senderPeerVersion;
            RequestSeqNum = requestSeqNum;
            IsResponse = isResponse;
        }
        /// <summary>
        /// A number from the sender, to be sent back with the response so that the sender can distinguish out-of-order / mistaken / sporadic messages from the specific response requested (e.g. if a send retry occurs and then the reply for the previous send comes; such a message needs to be discarded)
        /// </summary>
        [DataMember]
        public int RequestSeqNum { get; private set; }
        [DataMember]
        public bool IsResponse { get; set; } = false;
        /// <summary>
        /// The version which the sender of this message is currently up to. Any updates to versions prior to this can be discarded, and this is also used to check that the shadow is consistent from peer to peer
        /// </summary>
        [DataMember]
        public int SenderPeerVersion { get; private set; }
        /// <summary>
       /// A copy of the shadow on the other side for debugging
        /// </summary>
        [DataMember]
        public D DebugShadow { get; private set; }

    }
}
