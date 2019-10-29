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
    public class ExampleDiffSyncable : DiffSync.NET.IDiffSyncable<Dictionary<string, object>, ExampleDiff>, INotifyPropertyChanged
    {
        private static object initializeLock = new object();
        private static List<FieldInfo> Fields = null;
        private static List<PropertyInfo> Properties = null;

        private static void GenerateReflectionData()
        {
            lock(initializeLock)
            {
                if (Properties == null)
                {
                    Properties = typeof(ExampleDiffSyncable).GetProperties().Where(prop => Attribute.IsDefined(prop, typeof(DiffSyncAttribute))).ToList();
                    Fields = typeof(ExampleDiffSyncable).GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance).Where(prop => Attribute.IsDefined(prop, typeof(DiffSyncAttribute))).ToList();
                }
            }
            
        }
        [DataMember]
        private string _A = "A value";
        [DiffSync]
        public string A
        {
            get => _A;
            set
            {
                if (_A != value)
                {
                    _A = value;
                    OnPropertyChanged("A");
                }
            }
        }
        [DataMember]
        private string _B = "B value";
        [DiffSync]
        public string B
        {
            get => _B;
            set
            {
                if (_B != value)
                {
                    _B = value;
                    OnPropertyChanged("B");
                }
            }
        }
        [DataMember]
        private string _C = "C value";
        [DiffSync]
        public string C
        {
            get => _C;
            set
            {
                if (_C != value)
                {
                    _C = value;
                    OnPropertyChanged("C");
                }
            }
        }

        [DataMember]
        private byte[] _Ink = null;
        [DiffSync]
        public byte[] Ink
        {
            get => _Ink;
            set
            {
                if (_Ink != value)
                {
                    _Ink = value;
                    OnPropertyChanged("Ink");
                }
            }
        }
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        public override string ToString() => A + B + C;
        private static Dictionary<Point, Stroke> ByteToStrokes(byte[] mem)
        {
            if (mem == null || mem.Length == 0) return null;
            using (var ms = new MemoryStream(mem))
            {
                // Remove any strokes that start from the exact same spot
                return new StrokeCollection(ms).Where(s => s.StylusPoints.Count > 0).GroupBy(s => s.StylusPoints[0].ToPoint()).ToDictionary(p => p.Key, p => p.First());
            }
        }
        private static byte[] StrokeToBytes(StrokeCollection strokes)
        {

            using (var ms = new MemoryStream())
            {
                // Remove any strokes that start from the exact same spot
                strokes.Save(ms);
                ms.Position = 0;
                return ms.ToArray();
            }
        }
        public Dictionary<string, object> GetStateData()
        {
            if( Properties == null ) GenerateReflectionData();

            var state = new Dictionary<string, object>();
            foreach (var p in Properties)
                state.Add(p.Name, p.GetValue(this));

            foreach (var p in Fields)
                state.Add(p.Name, p.GetValue(this));

            return state;
        }
        public DiffSync.NET.IDiffSyncable<Dictionary<string, object>, ExampleDiff> Clone()
        {
            return new ExampleDiffSyncable()
            {
                A = A,
                B = B,
                C = C,
                Ink = Ink
            };
        }

        public ExampleDiff GetDiff(int version, Dictionary<string, object> otherState)
        {
            var aData = GetStateData();
            var bData = otherState;
            Dictionary<string,object> diffData ;
            if (bData != null)
                diffData = aData.Where(av => !bData.ContainsKey(av.Key) || ((av.Key == "Ink" && ((byte[])bData[av.Key])?.Length != ((byte[])av.Value)?.Length) || (av.Key != "Ink" && !bData[av.Key].Equals(av.Value)))).Union(bData.Where(bv => !aData.ContainsKey(bv.Key))).ToDictionary(av => av.Key, av => av.Value);
            else
                return null;

            if (diffData.Count == 0) return null;

            return new ExampleDiff(diffData)
            {
                Version = version
            };
        }
        public void Apply(ExampleDiff patchData)
        {

            foreach (var prop in Properties.Where(pr => patchData.ContainsKey(pr.Name)))
            {
                if (prop.Name == "Ink")
                {
                    var ink = ByteToStrokes((byte[])prop.GetValue(this));
                    var ink2 = ByteToStrokes((byte[])patchData["Ink"]);

                    var resInk = ink;
                    if (ink == null && ink2 == null)
                        resInk = null;
                    else if (ink == null)
                        resInk = ink2;
                    else if (ink2 == null)
                        resInk = ink;
                    else
                        resInk = ink.Union(ink2).GroupBy(s => s.Key).ToDictionary(s => s.Key, s => s.OrderByDescending(st => st.Value.StylusPoints.Count).Select(st => st.Value).First());

                    if (resInk != null)
                    {
                        var sc = new StrokeCollection(resInk.Values);
                        prop.SetValue(this, StrokeToBytes(sc));
                    }
                    else
                    {
                        prop.SetValue(this, null);
                    }
                }
                else
                    prop.SetValue(this, patchData[prop.Name]);
            }

            foreach (var field in Fields.Where(pr => patchData.ContainsKey(pr.Name)))
                field.SetValue(this, patchData[field.Name]);
        }
    }
}
