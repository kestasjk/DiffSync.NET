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
    public class ExampleDiff : Dictionary<string, object>, IDiff
    {
        [DataMember]
        public int Version { get; set; }

        public ExampleDiff(IDictionary<string, object> data ) : base(data) { }
        public override string ToString()
        {
            if (Count == 0)
                return Version.ToString()+" Empty";
            else
                return Version.ToString() + " " + this.Select(kvp => kvp.Key + "=" + kvp.Value).Aggregate((a, b) => a + ", " + b);
        }
    }
}
