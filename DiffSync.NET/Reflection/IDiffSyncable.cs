using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiffSync.NET.Reflection
{
    /// <summary>
    /// An interface which allows an object to be used as the state by copying itself, and other functions needed for a reflection based syncer
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IReflectionSyncable<T>
    {
        /// <summary>
        /// It should not be possible to call this function and meanwhile change the state. Note this does change the state, it just returns
        /// itself for convenience when cloning.
        /// </summary>
        /// <param name="copyFromObj"></param>
        /// <returns>this</returns>
        T CopyStateFrom(T copyFromObj);
        /// <summary>
        /// This is locked while CopyStateFrom runs; ensure that either updates to this object cannot happen while doing a CopyStateFrom, or else ensure this is locked while updating.
        /// DiffSync.NET does not use this lock, it is put in this interface to make clear a lock is needed.
        /// Note that within the CopyStateFrom both the object being copied and the object being copied to should be locked.
        /// </summary>
        object CopyStateFromLock { get; }
        /// <summary>
        /// A revision number; 0 means it does not exist in the database yet. This is v important for ensuring the server and client are making changes to the same shadow state.
        /// </summary>
        int Revision { get; }

        /// <summary>
        /// When was this item last updated outside the diff-sync process
        /// </summary>
        DateTime LastUpdated { get; }

    }
}
