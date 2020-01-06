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
    /// This is the state which I consider my peer's LiveState to be at, until I get a set of edits that show otherwise.
    /// </summary>
    [DataContract]
    public class ShadowState<T, D, S> : State<T, D, S> where T : class, IDiffSyncable<S,D>, new() where D : class, IDiff where S : class
    {
        public ShadowState(T obj) : base(obj) { }

        /// <summary>
        /// The server/peer version
        /// </summary>
        [DataMember]
        public int PeerVersion { get; set; } = 0;

        /// <summary>
        /// Take a checksum of the state. This is used to send to the server(/peer if symmetric) so they can recognise that their LiveState
        /// does not match the client's ShadowState, so something went wrong.
        /// </summary>
        /// <returns></returns>
        //public string Checksum()
        //{
        //    this.LockAndCopy();
        //}
    }
}
