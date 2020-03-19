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
    /// Applying this attribute to a property will flag that it should be included in the list of DiffSynced fields on an object
    /// </summary>
    public class DiffSyncAttribute : Attribute
    {
    }
    /// <summary>
    /// In a client-server conflict the client wins
    /// </summary>
    public class DiffSyncPriorityToClientAttribute : Attribute
    {
    }
    /// <summary>
    /// In a client-server conflict the server wins
    /// </summary>
    public class DiffSyncPriorityToServerAttribute : Attribute
    {
    }
    /// <summary>
    /// In a client-server conflict the winner is the most recent
    /// </summary>
    public class DiffSyncPriorityToLatestChange : Attribute
    {
    }
    /// <summary>
    /// In a conflict just accept the other's change; it isn't so important so just sync with minimal fuss
    /// </summary>
    public class DiffSyncNotImportant : Attribute
    {
    }
    /// <summary>
    /// If there is a conflict flag the syncer and add a warning message
    /// </summary>
    public class DiffSyncImportant : Attribute
    {
    }
    /// <summary>
    /// When applied to a byte[] type this notifies the server this is a StrokeCollection, and should be merged as such
    /// </summary>
    public class DiffSyncInkAttribute : Attribute
    {
    }
    /// <summary>
    /// This attribute will take the two lists from a diff and union distinct them
    /// </summary>
    public class DiffSyncUnionDistinctAttribute : Attribute
    {
    }
}
