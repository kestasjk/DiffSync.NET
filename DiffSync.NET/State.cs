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
    /// <summary>
    /// Represents the state of the object being tracked, along with a version and the last diffed state to allow diffs to be generated. Used for Live, Backup and BackupShadow to generate and apply diffs
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <typeparam name="D"></typeparam>
    /// <typeparam name="S"></typeparam>
    [DataContract]
    public abstract class State<T, D, S> where T : class, IDiffSyncable<S,D>, new() where D : class, IDiff where S : class
    {
        /// <summary>
        ///  This is the actual live object that we are tracking:
        /// </summary>
        [DataMember]
        public T StateObject { get; protected set; }

        /// <summary>
        /// This is the last polled state, so we can get a diff against the last diff
        /// </summary>
        [DataMember]
        private S LatestPolledState = null;

        /// <summary>
        /// The version for the algorithm purposes
        /// </summary>
        [DataMember]
        public int Version { get; set; } = 0;

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
            var currentState = StateObject.GetStateData();

            // If the latest polled state is null this returns null
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

        public virtual void Apply(D _patch, bool? isResponse, bool isShadowApply) => StateObject.Apply(_patch, isResponse, isShadowApply);
    }
}
