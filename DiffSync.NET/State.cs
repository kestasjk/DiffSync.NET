using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
    // This implementation uses [DiffSync] tags on fields / properties with cached reflected members to extract values, and assuming they are comparable
    public abstract class StateManager<T, D, S> where T : class, IDiffSyncable, new() where D : Diff where S : StateDataDictionary
    {
        // This is the actual live object that we are tracking:
        public T StateObject { get; set; }

        // Our version
        public int Version { get; set; } = 0;


        /// <summary>
        /// A function which will be used to create a diff.
        /// The int is the Version + 1 , 
        /// The first S is the current State
        /// The second S is the LatestPolledState
        /// Returned is a diff, or null if a diff was not possible/necessary
        /// </summary>
        public Func<int, S, S, D> CreateDiff { get; set; }
        /// <summary>
        /// A function which will be used to create a diff.
        /// The int is the Version + 1 , 
        /// The first S is the current State
        /// The second S is the LatestPolledState
        /// Returned is a diff, or null if a diff was not possible/necessary
        /// </summary>
        public Func<int, S, S, S> CreateStateData { get; set; }
        /// <summary>
        /// A function which will be used to create a diff.
        /// The int is the Version + 1 , 
        /// The first S is the current State
        /// The second S is the LatestPolledState
        /// Returned is a diff, or null if a diff was not possible/necessary
        /// </summary>
        public Func<int, S, S,S> CopyStateData { get; set; }
        protected StateManager(T obj)
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

            var diff = CreateDiff(Version + 1, currentState, LatestPolledState); // Diff.Create(Version+1, currentState, LatestPolledState);
            LatestPolledState = currentState;
            if (diff == null) return null;

            var diffData = diff.GetData;
            if (diffData.Count == 0)
                return null;

            return diff;
        }

        internal D DiffAgainst(StateManager<T,D, S> other)
        {
            var currentState = QuickLockAndCopy();
            var otherState = other.QuickLockAndCopy();
            var diff = CreateDiff(Version, currentState, otherState);// Diff.Create(Version, currentState, otherState);
            
            var diffData = diff.GetData;
            if (diffData.Count == 0)
                return null;
            else
                Version = diff.Version;

            return diff;
        }

        S LatestPolledState;

        // The downside of this global cache is the global lock (but it should be rarely if ever [after startup] needed)
        private static object initializeLock = new object();
        private static Dictionary<Type, List<FieldInfo>> Fields = new Dictionary<Type, List<FieldInfo>>();
        private static Dictionary<Type, List<PropertyInfo>> Properties = new Dictionary<Type, List<PropertyInfo>>();

        private object copyLock = new object();
        public S QuickLockAndCopy()
        {
            
            return CopyStateData(Version,this.);
        }

        private static Dictionary<Point, Stroke> ByteToStrokes(byte[] mem)
        {
            if (mem == null || mem.Length == 0) return null;
            using (var ms = new MemoryStream(mem))
            {
                // Remove any strokes that start from the exact same spot
                return new StrokeCollection(ms).Where(s => s.StylusPoints.Count > 0).GroupBy(s => s.StylusPoints[0].ToPoint()).ToDictionary(p=>p.Key, p => p.First());
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
        public virtual void Apply(Patch _patch)
        {
            var patch = _patch.GetData;
            var type = typeof(T);

            foreach (var prop in Properties[type].Where(pr => patch.ContainsKey(pr.Name)))
            {
                if (prop.Name == "Ink")
                {
                    var ink = ByteToStrokes((byte[])prop.GetValue(StateObject));
                    var ink2 = ByteToStrokes((byte[])patch["Ink"]);

                    var resInk = ink;
                    if (ink == null && ink2 == null)
                        resInk = null;
                    else if (ink == null)
                        resInk = ink2;
                    else if (ink2 == null)
                        resInk = ink;
                    else
                    {

                        resInk = ink.Union(ink2).GroupBy(s => s.Key).ToDictionary(s => s.Key, s => s.OrderByDescending(st => st.Value.StylusPoints.Count).Select(st => st.Value).First());

                    }

                    if( resInk != null )
                    {
                        var sc = new StrokeCollection(resInk.Values);
                        prop.SetValue(StateObject, StrokeToBytes(sc));
                    }
                    else
                    {
                        prop.SetValue(StateObject, null);
                    }
                }
                else
                    prop.SetValue(StateObject, patch[prop.Name]);
            }

            foreach (var field in Fields[type].Where(pr => patch.ContainsKey(pr.Name)))
                field.SetValue(StateObject, patch[field.Name]);

            //Version = _patch.Version;
        }
    }
}
