using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Ink;

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
    public abstract class State<T, D, S> where T : class, IDiffSyncable<S,D>, new() where D : class, IDiff where S : class
    {
        // This is the actual live object that we are tracking:
        [DataMember]
        public T StateObject { get; protected set; }

        [DataMember]
        private S LatestPolledState= null;

        // Our version
        [DataMember]
        public int Version { get; set; } = 0;

        private object LockObject = new object();
        protected State(T obj)
        {
            // This could keep a appdomain-wide cache if it were static?
            StateObject = obj;//.Clone() as T;
        }

        /// <summary>
        /// Each diff from this function will increment the version number
        /// </summary>
        /// <returns></returns>
        internal D PollForLocalDifferencesOrNull()
        {
            var currentState = QuickLockAndCopy();

            var diff = StateObject.GetDiff(Version + 1, LatestPolledState); // Diff.Create(Version+1, currentState, LatestPolledState);
            LatestPolledState = currentState;
            if (diff == null) return null;

            return diff;
        }

        internal D DiffAgainst(State<T,D, S> other)
        {
            var otherState = other.StateObject.GetStateData();
            var diff = StateObject.GetDiff(Version, otherState);// Diff.Create(Version, currentState, otherState);
            
            if( diff != null)
                Version = diff.Version;

            return diff;
        }

        public S QuickLockAndCopy()
        {
            return StateObject.GetStateData();
        }

        public virtual void Apply(D _patch)
        {
            StateObject.Apply(_patch);
        }
    }
}
