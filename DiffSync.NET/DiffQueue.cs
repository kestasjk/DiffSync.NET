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
    public class DiffQueue<D> where D : Diff
    {
        private object changeLock = new object();

        public List<D> Diffs { get; protected set; } = new List<D>();

        public IEnumerable<D> Get()
        {
            lock (Diffs) return Diffs.ToList();
        }

        public void Add(D o)
        {
            lock (changeLock) Diffs.Add(o);
        }
        public void OnEditsReceived(int clientVersion)
        {
            lock (changeLock) Diffs.RemoveAll(diff => diff.Version <= clientVersion);
        }
    }
}
