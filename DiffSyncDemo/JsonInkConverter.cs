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
            if ( objectType == typeof(Dictionary<string,object>))
                return true;
            else
                return false;
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if( objectType == typeof(Dictionary<string, object>))
            {
                var dict = new Dictionary<string, object>();
                if(reader.TokenType == JsonToken.StartObject)
                {
                    reader.Read();

                    while( reader.TokenType == JsonToken.PropertyName)
                    {
                        var key = (string) reader.Value;
                        reader.Read();

                        if( reader.TokenType == JsonToken.String)
                        {
                            var value = reader.Value;
                            dict.Add(key, value);
                        }
                        else if ( reader.TokenType == JsonToken.Null)
                        {
                            object value = null;
                            dict.Add(key, value);
                        }
                        else
                        {
                            throw new Exception("Unexpected type to deserialize");
                        }

                        reader.Read();
                    }

                    if(reader.TokenType == JsonToken.EndObject)
                    {
                        //reader.Read();
                    }
                }
                else if( reader.TokenType == JsonToken.Null)
                {
                    return null;
                }
                else
                {
                    throw new Exception("Unexpected token type for a dictionary");
                }

                // Now convert the read dictionary into a proper one;
                var readDict = new Dictionary<string, object>();

                foreach(var kvp in dict)
                {
                    if( kvp.Value == null )
                    {
                        readDict.Add(kvp.Key, null);
                    }
                    else if ( kvp.Value.GetType() == typeof(string) )
                    {
                        var strVal = (string) kvp.Value;
                        if( strVal.StartsWith("InkBytes:") )
                        {
                            readDict.Add(kvp.Key, System.Convert.FromBase64String(strVal.Replace("InkBytes:", "")));
                        }
                        else
                        {
                            readDict.Add(kvp.Key, kvp.Value);
                        }
                    }
                    else
                    {
                        readDict.Add(kvp.Key, kvp.Value);
                    }
                }

                return readDict;
            }
            return null;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if( value.GetType() == typeof(Dictionary<string, object>))
            {
                var dict = (Dictionary<string, object>)value;
                writer.WriteStartObject();

                foreach(var kvp in dict)
                {
                    writer.WritePropertyName(kvp.Key);
                    if( kvp.Value == null )
                    {
                        writer.WriteNull();
                    }
                    else if( kvp.Value.GetType() == typeof(byte[]))
                    {
                        byte[] val = (byte[])kvp.Value;
                        writer.WriteValue("InkBytes:" + System.Convert.ToBase64String(val));
                    }
                    else
                    {
                        writer.WriteValue(kvp.Value);
                    }
                }
                writer.WriteEndObject();
            }
        }
    }
}
