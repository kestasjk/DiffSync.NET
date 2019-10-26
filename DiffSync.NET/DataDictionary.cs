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
    public interface IDataDictionary
    {
        IReadOnlyDictionary<string,object> GetData { get; }
    }
    [DataContract]
    public abstract class DataDictionary : IDataDictionary
    {
        [DataMember]
        private IReadOnlyDictionary<string, object> StateData;
        public IReadOnlyDictionary<string, object> GetData => StateData;
        internal DataDictionary(IReadOnlyDictionary<string, object> data)
        {
            StateData = data;
        }
        public override string ToString()
        {
            if (StateData.Count == 0)
                return "Empty";
            else
                return StateData.Select(kvp => kvp.Key + "=" + kvp.Value).Aggregate((a, b) => a + ", " + b);
        }
    }
}
