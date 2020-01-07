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
    /// S represents the class containing the state. This could be a dictionary, a diff file, or an object. To generate a diff and apply a diff is the main requirement
    /// </summary>
    /// <typeparam name="S"></typeparam>
    /// <typeparam name="D"></typeparam>
    public interface IDiffSyncable<S,D> where D : IDiff where S : class
    {

        S GetStateData();
        D GetDiff(int version, S o);
        /// <summary>
        /// If isResponse = null then this is a local diff, otherwise if false this is a client making the change to the server, if true it is the server making the change to the client
        /// </summary>
        /// <param name="data"></param>
        /// <param name="isResponse"></param>
        void Apply(D data, bool? isResponse);
    }
}
