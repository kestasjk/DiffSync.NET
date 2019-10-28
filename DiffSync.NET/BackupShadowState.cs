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
    /// <summary>
    /// This is the backup state for initialization and if an edit gets lost while a following edit is not lost. 
    /// 
    /// This would override our LiveState and ShadowState thinking that we have a new version, and we send a new version based on this, but at a later time we receive the older update
    /// </summary>
    public class BackupShadowState<T, D, S> : ShadowState<T, D, S> where T : class, IDiffSyncable<S,D>, new() where D : class, IDiff
    {
        public BackupShadowState(T obj) : base(obj)
        {
        }
        public BackupShadowState(ShadowState<T, D, S> obj) : base(obj.StateObject) {
            Version = obj.Version;
            PeerVersion = obj.PeerVersion;
        }

    }
}
