using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

#pragma warning disable 0649 // fields are filled in by DataContractJsonSerializer

namespace GooseDeluxe
{
    /// <summary>One JSON line from an ntfy subscription stream (only the fields we use).</summary>
    [DataContract]
    internal sealed class NtfyMessage
    {
        [DataMember(Name = "id")] public string id;
        [DataMember(Name = "time")] public long time;
        [DataMember(Name = "event")] public string @event;
        [DataMember(Name = "topic")] public string topic;
        [DataMember(Name = "message")] public string message;
        [DataMember(Name = "title")] public string title;
        [DataMember(Name = "tags")] public string[] tags;
        [DataMember(Name = "attachment")] public NtfyAttachment attachment;

        private static readonly DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(NtfyMessage));

        public static NtfyMessage Parse(string line)
        {
            if (string.IsNullOrEmpty(line) || line[0] != '{') return null;
            try
            {
                using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(line)))
                    return (NtfyMessage)serializer.ReadObject(ms);
            }
            catch
            {
                return null;
            }
        }
    }

    [DataContract]
    internal sealed class NtfyAttachment
    {
        [DataMember(Name = "name")] public string name;
        [DataMember(Name = "type")] public string type;
        [DataMember(Name = "size")] public long size;
        [DataMember(Name = "url")] public string url;
    }
}
