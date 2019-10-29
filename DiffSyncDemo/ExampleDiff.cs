using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Ink;
using DiffSync.NET;

namespace DiffSyncDemo
{
    [DataContract]
    public class ExampleDiff : IDiff
    {
        [DataMember]
        public int Version { get; set; }

        [DataMember]
        public Dictionary<string, object> DataDictionary { get; set; }

        public ExampleDiff(IDictionary<string, object> data ) {
            if (data != null)
                DataDictionary = new Dictionary<string, object>(data);
            else
                DataDictionary = new Dictionary<string, object>();
        }
        public override string ToString()
        {
            if (DataDictionary.Count == 0)
                return Version.ToString()+" Empty";
            else
                return Version.ToString() + " " + DataDictionary.Select(kvp => kvp.Key + "=" + kvp.Value).Aggregate((a, b) => a + ", " + b);
        }
    }
}
