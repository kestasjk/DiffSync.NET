using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiffSyncDemo
{
    /// <summary>
    /// A class to help with serializing and deserializing non-trivial objects in the 
    /// state data dictionary. In this demo this is the Ink object.
    /// </summary>
    class JsonInkConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            if (objectType == typeof(byte[]))
            {
                return true;
            }
            else if (objectType == typeof(string))
            {
                return true;
            }
            else
            { 
                return false;
            }
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if( objectType == typeof(string))
            {
                string val = (string)existingValue;
                if( val.StartsWith("InkBytes:"))
                {
                    return System.Convert.FromBase64String(val.Replace("InkBytes:", ""));
                }
                else
                {
                    return val;
                }
            }
            return null;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if( value.GetType() == typeof(byte[]) )
            {
                byte[] val = (byte[])value;
                writer.WriteValue("InkBytes:" + System.Convert.ToBase64String(val));
            }else
            {
                writer.WriteValue(value);
            }
        }
    }
}
