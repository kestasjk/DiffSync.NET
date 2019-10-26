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
    public interface IStateDataDictionary : IVersionedDataDictionary
    {
    }
    public class StateDataDictionary : VersionedDataDictionary
    {
        internal StateDataDictionary(int _version, IReadOnlyDictionary<string,object> data) : base(_version, data) { }
        public static StateDataDictionary Create(int _version, IReadOnlyDictionary<string, object> data) => new StateDataDictionary(_version, data);

    }
    public interface IVersionedDataDictionary : IDataDictionary
    {
        int Version { get; }
    }
    public class VersionedDataDictionary : DataDictionary, IVersionedDataDictionary
    {
        public int Version { get; internal set; }
        internal VersionedDataDictionary(int _version, IReadOnlyDictionary<string, object> data) : base(data) { Version = _version; }
        public override string ToString()
        {
            return "#"+Version+":"+base.ToString();
        }
    }
}
