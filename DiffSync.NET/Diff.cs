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
    public interface IDiff : IVersionedDataDictionary
    {

    }
    public class Diff : VersionedDataDictionary, IDiff
    {
        private Diff(int version, IReadOnlyDictionary<string, object> data) : base(version, data) { }

        public static Diff Create(int version, StateDataDictionary a, StateDataDictionary b)
        {
            var aData = a.GetData;
            var bData = b.GetData;

            //if (aData.ContainsKey("B"))
           //     Debugger.Break();

            var diff =  new Diff(version, aData.Where(av => !bData.ContainsKey(av.Key) 
            || (( av.Key == "Ink" && ((byte[])bData[av.Key])?.Length != ((byte[])av.Value)?.Length)|| (av.Key != "Ink" && !bData[av.Key].Equals(av.Value)))).Union(bData.Where(bv=>!aData.ContainsKey(bv.Key))).ToDictionary(av => av.Key, av => av.Value));

            
            return diff;
        }
    }
}
