﻿// #define DEBUG_LSF_SERIALIZATION

using LZ4;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace LSLib.LS.LSF
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct Header
    {
        /// <summary>
        /// LSOF file signature
        /// </summary>
        public static byte[] Signature = new byte[] { 0x4C, 0x53, 0x4F, 0x46 };

        /// <summary>
        /// LSOF compressed chunk signature
        /// </summary>
        public static byte[] ChunkSignature = new byte[] { 0x04, 0x22, 0x4D, 0x18, 0x40, 0x40, 0xC0 };

        /// <summary>
        /// Initial version of the LSF format
        /// </summary>
        public const UInt32 VerInitial = 0x01;

        /// <summary>
        /// LSF version that added chunked compression for substreams
        /// </summary>
        public const UInt32 VerChunkedCompress = 0x02;

        /// <summary>
        /// Latest version supported by this library
        /// </summary>
        public const UInt32 CurrentVersion = 0x02;

        /// <summary>
        /// LSOF file signature; should be the same as LSFHeader.Signature
        /// </summary>
        public UInt32 Magic;
        /// <summary>
        /// Version of the LSOF file; DO:S EE is version 1, no other versions were seen so far
        /// </summary>
        public UInt32 Version;
        /// <summary>
        /// Possibly version number? (major, minor, rev, build)
        /// </summary>
        public UInt32 Unknown;
        /// <summary>
        /// Total uncompressed size of the string hash table
        /// </summary>
        public UInt32 StringsUncompressedSize;
        /// <summary>
        /// Compressed size of the string hash table
        /// </summary>
        public UInt32 StringsSizeOnDisk;
        /// <summary>
        /// Total uncompressed size of the node list
        /// </summary>
        public UInt32 NodesUncompressedSize;
        /// <summary>
        /// Compressed size of the node list
        /// </summary>
        public UInt32 NodesSizeOnDisk;
        /// <summary>
        /// Total uncompressed size of the attribute list
        /// </summary>
        public UInt32 AttributesUncompressedSize;
        /// <summary>
        /// Compressed size of the attribute list
        /// </summary>
        public UInt32 AttributesSizeOnDisk;
        /// <summary>
        /// Total uncompressed size of the raw value buffer
        /// </summary>
        public UInt32 ValuesUncompressedSize;
        /// <summary>
        /// Compressed size of the raw value buffer
        /// </summary>
        public UInt32 ValuesSizeOnDisk;
        /// <summary>
        /// Compression method and level used for the string, node, attribute and value buffers.
        /// Uses the same format as packages (see BinUtils.MakeCompressionFlags)
        /// </summary>
        public Byte CompressionFlags;
        /// <summary>
        /// Possibly unused, always 0
        /// </summary>
        public Byte Unknown2;
        public UInt16 Unknown3;
        public UInt32 Unknown4;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct ChunkHeader
    {
        /// <summary>
        /// LSOF compressed chunk signature
        /// </summary>
        public static byte[] Signature = new byte[] { 0x04, 0x22, 0x4D, 0x18 };

        /// <summary>
        /// LSOF chunk signature; should be the same as ChunkHeader.Signature
        /// </summary>
        public UInt32 Magic;
        /// <summary>
        /// Unknown flags
        /// </summary>
        public Byte Flags1;
        /// <summary>
        /// Unknown flags
        /// </summary>
        public Byte Flags2;
        /// <summary>
        /// Unknown flags
        /// </summary>
        public Byte Flags3;
    }

    /// <summary>
    /// Node (structure) entry in the LSF file
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct NodeEntry
    {
        /// <summary>
        /// Name of this node
        /// (16-bit MSB: index into name hash table, 16-bit LSB: offset in hash chain)
        /// </summary>
        public UInt32 NameHashTableIndex;
        /// <summary>
        /// Index of the first attribute of this node
        /// (-1: node has no attributes)
        /// </summary>
        public Int32 FirstAttributeIndex;
        /// <summary>
        /// Index of the parent node
        /// (-1: this node is a root region)
        /// </summary>
        public Int32 ParentIndex;

        /// <summary>
        /// Index into name hash table
        /// </summary>
        public int NameIndex
        {
            get { return (int)(NameHashTableIndex >> 16); }
        }

        /// <summary>
        /// Offset in hash chain
        /// </summary>
        public int NameOffset
        {
            get { return (int)(NameHashTableIndex & 0xffff); }
        }
    };

    /// <summary>
    /// Processed node information for a node in the LSF file
    /// </summary>
    internal class NodeInfo
    {
        /// <summary>
        /// Index of the parent node
        /// (-1: this node is a root region)
        /// </summary>
        public int ParentIndex;
        /// <summary>
        /// Index into name hash table
        /// </summary>
        public int NameIndex;
        /// <summary>
        /// Offset in hash chain
        /// </summary>
        public int NameOffset;
        /// <summary>
        /// Index of the first attribute of this node
        /// (-1: node has no attributes)
        /// </summary>
        public int FirstAttributeIndex;
    };

    /// <summary>
    /// Attribute entry in the LSF file
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct AttributeEntry
    {
        /// <summary>
        /// Name of this attribute
        /// (16-bit MSB: index into name hash table, 16-bit LSB: offset in hash chain)
        /// </summary>
        public UInt32 NameHashTableIndex;
        /// <summary>
        /// 6-bit LSB: Type of this attribute (see NodeAttribute.DataType)
        /// 26-bit MSB: Length of this attribute
        /// </summary>
        public UInt32 TypeAndLength;
        /// <summary>
        /// Index of the node that this attribute belongs to
        /// Note: These indexes are assigned seemingly arbitrarily, and are not neccessarily indices into the node list
        /// </summary>
        public Int32 NodeIndex;

        /// <summary>
        /// Index into name hash table
        /// </summary>
        public int NameIndex
        {
            get { return (int)(NameHashTableIndex >> 16); }
        }

        /// <summary>
        /// Offset in hash chain
        /// </summary>
        public int NameOffset
        {
            get { return (int)(NameHashTableIndex & 0xffff); }
        }

        /// <summary>
        /// Type of this attribute (see NodeAttribute.DataType)
        /// </summary>
        public uint TypeId
        {
            get { return TypeAndLength & 0x3f; }
        }

        /// <summary>
        /// Length of this attribute
        /// </summary>
        public uint Length
        {
            get { return TypeAndLength >> 6; }
        }
    };

    internal class AttributeInfo
    {
        /// <summary>
        /// Index into name hash table
        /// </summary>
        public int NameIndex;
        /// <summary>
        /// Offset in hash chain
        /// </summary>
        public int NameOffset;
        /// <summary>
        /// Type of this attribute (see NodeAttribute.DataType)
        /// </summary>
        public uint TypeId;
        /// <summary>
        /// Length of this attribute
        /// </summary>
        public uint Length;
        /// <summary>
        /// Absolute position of attribute data in the values section
        /// </summary>
        public uint DataOffset;
        /// <summary>
        /// Index of the next attribute in this node
        /// (-1: this is the last attribute)
        /// </summary>
        public int NextAttributeIndex;
    };

    public class LSFReader : IDisposable
    {
        /// <summary>
        /// Input stream
        /// </summary>
        private Stream Stream;

        /// <summary>
        /// Static string hash map
        /// </summary>
        private List<List<String>> Names;
        /// <summary>
        /// Preprocessed list of nodes (structures)
        /// </summary>
        private List<NodeInfo> Nodes;
        /// <summary>
        /// Preprocessed list of node attributes
        /// </summary>
        private List<AttributeInfo> Attributes;
        /// <summary>
        /// Node instances
        /// </summary>
        private List<Node> NodeInstances;
        /// <summary>
        /// Raw value data stream
        /// </summary>
        private Stream Values;

        public LSFReader(Stream stream)
        {
            this.Stream = stream;
        }

        public void Dispose()
        {
            Stream.Dispose();
        }

        /// <summary>
        /// Reads the static string hash table from the specified stream.
        /// </summary>
        /// <param name="s">Stream to read the hash table from</param>
        private void ReadNames(Stream s)
        {
#if DEBUG_LSF_SERIALIZATION
            Console.WriteLine(" ----- DUMP OF NAME TABLE -----");
#endif

            // Format:
            // 32-bit hash entry count (N)
            //     N x 16-bit chain length (L)
            //         L x 16-bit string length (S)
            //             [S bytes of UTF-8 string data]

            Names = new List<List<String>>();
            using (var reader = new BinaryReader(s))
            {
                var numHashEntries = reader.ReadUInt32();
                while (numHashEntries-- > 0)
                {
                    var hash = new List<String>();
                    Names.Add(hash);

                    var numStrings = reader.ReadUInt16();
                    while (numStrings-- > 0)
                    {
                        var nameLen = reader.ReadUInt16();
                        byte[] bytes = reader.ReadBytes(nameLen);
                        var name = System.Text.Encoding.UTF8.GetString(bytes);
                        hash.Add(name);
#if DEBUG_LSF_SERIALIZATION
                        Console.WriteLine(String.Format("{0,3:X}/{1}: {2}", Names.Count - 1, hash.Count - 1, name));
#endif
                    }
                }
            }
        }

        /// <summary>
        /// Reads the structure headers for the LSOF resource
        /// </summary>
        /// <param name="s">Stream to read the node headers from</param>
        private void ReadNodes(Stream s)
        {
#if DEBUG_LSF_SERIALIZATION
            Console.WriteLine(" ----- DUMP OF NODE TABLE -----");
#endif

            Nodes = new List<NodeInfo>();
            using (var reader = new BinaryReader(s))
            {
                Int32 index = 0;
                while (s.Position < s.Length)
                {
                    var item = BinUtils.ReadStruct<NodeEntry>(reader);

                    var resolved = new NodeInfo();
                    resolved.ParentIndex = item.ParentIndex;
                    resolved.NameIndex = item.NameIndex;
                    resolved.NameOffset = item.NameOffset;
                    resolved.FirstAttributeIndex = item.FirstAttributeIndex;

#if DEBUG_LSF_SERIALIZATION
                    Console.WriteLine(String.Format(
                        "{0}: {1} (parent {2}, firstAttribute {3})", 
                        Nodes.Count, Names[resolved.NameIndex][resolved.NameOffset], resolved.ParentIndex, resolved.FirstAttributeIndex
                    ));
#endif

                    Nodes.Add(resolved);
                    index++;
                }
            }
        }

        /// <summary>
        /// Reads the attribute headers for the LSOF resource
        /// </summary>
        /// <param name="s">Stream to read the attribute headers from</param>
        private void ReadAttributes(Stream s)
        {
            Attributes = new List<AttributeInfo>();
            using (var reader = new BinaryReader(s))
            {
#if DEBUG_LSF_SERIALIZATION
                var rawAttributes = new List<AttributeElement>();
#endif

                var prevAttributeRefs = new List<Int32>();
                UInt32 dataOffset = 0;
                Int32 index = 0;
                while (s.Position < s.Length)
                {
                    var attribute = BinUtils.ReadStruct<AttributeEntry>(reader);

                    var resolved = new AttributeInfo();
                    resolved.NameIndex = attribute.NameIndex;
                    resolved.NameOffset = attribute.NameOffset;
                    resolved.TypeId = attribute.TypeId;
                    resolved.Length = attribute.Length;
                    resolved.DataOffset = dataOffset;
                    resolved.NextAttributeIndex = -1;

                    var nodeIndex = attribute.NodeIndex + 1;
                    if (prevAttributeRefs.Count > nodeIndex)
                    {
                        if (prevAttributeRefs[nodeIndex] != -1)
                        {
                            Attributes[prevAttributeRefs[nodeIndex]].NextAttributeIndex = index;
                        }

                        prevAttributeRefs[nodeIndex] = index;
                    }
                    else
                    {
                        while (prevAttributeRefs.Count < nodeIndex)
                        {
                            prevAttributeRefs.Add(-1);
                        }

                        prevAttributeRefs.Add(index);
                    }

#if DEBUG_LSF_SERIALIZATION
                    rawAttributes.Add(attribute);
#endif

                    dataOffset += resolved.Length;
                    Attributes.Add(resolved);
                    index++;
                }

#if DEBUG_LSF_SERIALIZATION
                Console.WriteLine(" ----- DUMP OF ATTRIBUTE REFERENCES -----");
                for (int i = 0; i < prevAttributeRefs.Count; i++)
                {
                    Console.WriteLine(String.Format("Node {0}: last attribute {1}", i, prevAttributeRefs[i]));
                }


                Console.WriteLine(" ----- DUMP OF ATTRIBUTE TABLE -----");
                for (int i = 0; i < Attributes.Count; i++)
                {
                    var resolved = Attributes[i];
                    var attribute = rawAttributes[i];

                    var debug = String.Format(
                        "{0}: {1} (offset {2:X}, typeId {3}, nextAttribute {4}, node {5})",
                        i, Names[resolved.NameIndex][resolved.NameOffset], resolved.DataOffset,
                        resolved.TypeId, resolved.NextAttributeIndex, attribute.NodeIndex
                    );
                    Console.WriteLine(debug);
                }
#endif
            }
        }

        private byte[] Decompress(BinaryReader reader, uint compressedSize, uint uncompressedSize, Header header)
        {
            bool chunked = (header.Version >= Header.VerChunkedCompress);
            byte[] compressed = reader.ReadBytes((int)compressedSize);
            return BinUtils.Decompress(compressed, (int)uncompressedSize, header.CompressionFlags, chunked);
        }

        public Resource Read()
        {
            using (var reader = new BinaryReader(Stream))
            {
                var hdr = BinUtils.ReadStruct<Header>(reader);
                if (hdr.Magic != BitConverter.ToUInt32(Header.Signature, 0))
                {
                    var msg = String.Format(
                        "Invalid LSF signature; expected {0,8:X}, got {1,8:X}",
                        BitConverter.ToUInt32(Header.Signature, 0), hdr.Magic
                    );
                    throw new InvalidDataException(msg);
                }

                if (hdr.Version < Header.VerInitial || hdr.Version > Header.CurrentVersion)
                {
                    var msg = String.Format("LSF version {0} is not supported", hdr.Version);
                    throw new InvalidDataException(msg);
                }

                if (hdr.StringsSizeOnDisk > 0)
                {
                    byte[] compressed = reader.ReadBytes((int)hdr.StringsSizeOnDisk);
                    var uncompressed = BinUtils.Decompress(compressed, (int)hdr.StringsUncompressedSize, hdr.CompressionFlags);
                    using (var namesStream = new MemoryStream(uncompressed))
                    {
                        ReadNames(namesStream);
                    }
                }

                if (hdr.NodesSizeOnDisk > 0)
                {
                    var uncompressed = Decompress(reader, hdr.NodesSizeOnDisk, hdr.NodesUncompressedSize, hdr);
                    using (var nodesStream = new MemoryStream(uncompressed))
                    {
                        ReadNodes(nodesStream);
                    }
                }

                if (hdr.AttributesSizeOnDisk > 0)
                {
                    var uncompressed = Decompress(reader, hdr.AttributesSizeOnDisk, hdr.AttributesUncompressedSize, hdr);
                    using (var f = new FileStream("C:\\dbg.bin", FileMode.OpenOrCreate)) f.Write(uncompressed, 0, uncompressed.Length);
                    using (var attributesStream = new MemoryStream(uncompressed))
                    {
                        ReadAttributes(attributesStream);
                    }
                }

                if (hdr.ValuesSizeOnDisk > 0)
                {
                    var uncompressed = Decompress(reader, hdr.ValuesSizeOnDisk, hdr.ValuesUncompressedSize, hdr);
                    var valueStream = new MemoryStream(uncompressed);
                    this.Values = valueStream;

#if DEBUG_LSF_SERIALIZATION
                    using (var valuesFile = new FileStream("values.bin", FileMode.Create, FileAccess.Write))
                    {
                        valuesFile.Write(uncompressed, 0, uncompressed.Length);
                    }
#endif
                }
                else
                {
                    this.Values = new MemoryStream();
                }

                Resource resource = new Resource();
                ReadRegions(resource);
                return resource;
            }
        }

        private void ReadRegions(Resource resource)
        {
            var attrReader = new BinaryReader(Values);
            NodeInstances = new List<Node>();
            for (int i = 0; i < Nodes.Count; i++)
            {
                var defn = Nodes[i];
                if (defn.ParentIndex == -1)
                {
                    var region = new Region();
                    ReadNode(defn, region, attrReader);
                    NodeInstances.Add(region);
                    region.RegionName = region.Name;
                    resource.Regions[region.Name] = region;
                }
                else
                {
                    var node = new Node();
                    ReadNode(defn, node, attrReader);
                    node.Parent = NodeInstances[defn.ParentIndex];
                    NodeInstances.Add(node);
                    NodeInstances[defn.ParentIndex].AppendChild(node);
                }
            }
        }

        private void ReadNode(NodeInfo defn, Node node, BinaryReader attributeReader)
        {
            node.Name = Names[defn.NameIndex][defn.NameOffset];
            
#if DEBUG_LSF_SERIALIZATION
            Console.WriteLine(String.Format("Begin node {0}", node.Name));
#endif

            if (defn.FirstAttributeIndex != -1)
            {
                var attribute = Attributes[defn.FirstAttributeIndex];
                while (true)
                {
                    Values.Position = attribute.DataOffset;
                    var value = ReadAttribute((NodeAttribute.DataType)attribute.TypeId, attributeReader, attribute.Length);
                    node.Attributes[Names[attribute.NameIndex][attribute.NameOffset]] = value;

#if DEBUG_LSF_SERIALIZATION
                    Console.WriteLine(String.Format("    {0:X}: {1} ({2})", attribute.DataOffset, Names[attribute.NameIndex][attribute.NameOffset], value));
#endif

                    if (attribute.NextAttributeIndex == -1)
                    {
                        break;
                    }
                    else
                    {
                        attribute = Attributes[attribute.NextAttributeIndex];
                    }
                }
            }
        }

        private NodeAttribute ReadAttribute(NodeAttribute.DataType type, BinaryReader reader, uint length)
        {
            // LSF and LSB serialize the buffer types differently, so specialized
            // code is added to the LSB and LSf serializers, and the common code is
            // available in BinUtils.ReadAttribute()
            switch (type)
            {
                case NodeAttribute.DataType.DT_String:
                case NodeAttribute.DataType.DT_Path:
                case NodeAttribute.DataType.DT_FixedString:
                case NodeAttribute.DataType.DT_LSString:
                case NodeAttribute.DataType.DT_WString:
                case NodeAttribute.DataType.DT_LSWString:
                { 
                    var attr = new NodeAttribute(type);
                    attr.Value = ReadString(reader, (int)length);
                    return attr;
                }

                case NodeAttribute.DataType.DT_TranslatedString:
                {
                    var attr = new NodeAttribute(type);
                    var str = new TranslatedString();
                    var valueLength = reader.ReadInt32();
                    str.Value = ReadString(reader, valueLength);
                    var handleLength = reader.ReadInt32();
                    str.Handle = ReadString(reader, handleLength);
                    attr.Value = str;
                    return attr;
                }

                case NodeAttribute.DataType.DT_ScratchBuffer:
                { 
                    var attr = new NodeAttribute(type);
                    attr.Value = reader.ReadBytes((int)length);
                    return attr;
                }

                default:
                    return BinUtils.ReadAttribute(type, reader);
            }
        }

        private string ReadString(BinaryReader reader, int length)
        {
            var bytes = reader.ReadBytes(length - 1);
            var nullTerminator = reader.ReadByte();
            if (nullTerminator != 0)
            {
                throw new InvalidDataException("String is not null-terminated");
            }

            return Encoding.UTF8.GetString(bytes);
        }

        private string ReadString(BinaryReader reader)
        {
            List<byte> bytes = new List<byte>();
            while (true)
            {
                var b = reader.ReadByte();
                if (b != 0)
                {
                    bytes.Add(b);
                }
                else
                {
                    break;
                }
            }

            return Encoding.UTF8.GetString(bytes.ToArray());
        }
    }
}
