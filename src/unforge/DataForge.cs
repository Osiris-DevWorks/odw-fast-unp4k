using Dolkens.Framework.BinaryExtensions;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace unforge
{
	public class DataForge : BinaryReader
	{
		public Boolean IsLegacy;
		public Boolean FollowReferences { get => DataForge.MaxReferenceDepth > this.StructStack.Count; }
		public Boolean FollowWeakPointers { get; } = false;
		public Boolean FollowStrongPointers { get => DataForge.MaxPointerDepth > this.RecordStack.Count; }

		public static Int32 MaxReferenceDepth { get; set; } = 1;
		public static Int32 MaxPointerDepth { get; set; } = 100;
		public static Int32 MaxNodes { get; set; } = 10000;

		public Int32 FileVersion { get; }
		public Int64 Length { get => this.BaseStream.Length; }
		public Int64 Position
		{
			get => this.BaseStream.Position;
			set => this.BaseStream.Position = value;
		}

		public Int32 StructDefinitionCount { get; }
		public Int32 PropertyDefinitionCount { get; }
		public Int32 EnumDefinitionCount { get; }
		public Int32 DataMappingCount { get; }
		public Int32 RecordDefinitionCount { get; }

		public Int32 BooleanValueCount { get; }
		public Int32 Int8ValueCount { get; }
		public Int32 Int16ValueCount { get; }
		public Int32 Int32ValueCount { get; }
		public Int32 Int64ValueCount { get; }
		public Int32 UInt8ValueCount { get; }
		public Int32 UInt16ValueCount { get; }
		public Int32 UInt32ValueCount { get; }
		public Int32 UInt64ValueCount { get; }

		public Int32 SingleValueCount { get; }
		public Int32 DoubleValueCount { get; }
		public Int32 GuidValueCount { get; }
		public Int32 StringValueCount { get; }
		public Int32 LocaleValueCount { get; }
		public Int32 EnumValueCount { get; }
		public Int32 StrongValueCount { get; }
		public Int32 WeakValueCount { get; }

		public Int32 ReferenceValueCount { get; }
		public Int32 EnumOptionCount { get; }
		public UInt32 TextLength { get; }
		public UInt32 BlobLength { get; }

		private Int64 StructDefinitionOffset { get => this.IsLegacy ? 0x74 : 0x78; }
		public Int64 PropertyDefinitionOffset { get => this.StructDefinitionOffset + this.StructDefinitionCount * DataForgeStructDefinition.RecordSizeInBytes; }
		public Int64 EnumDefinitionOffset { get => this.PropertyDefinitionOffset + this.PropertyDefinitionCount * DataForgePropertyDefinition.RecordSizeInBytes; }
		public Int64 DataMappingOffset { get => this.EnumDefinitionOffset + this.EnumDefinitionCount * DataForgeEnumDefinition.RecordSizeInBytes; }
		public Int64 RecordDefinitionOffset { get => this.DataMappingOffset + this.DataMappingCount * (this.IsLegacy ? DataForgeDataMapping.RecordSizeInBytes : DataForgeDataMapping.RecordSizeInBytesV6); }
		public Int64 Int8ValueOffset { get => this.RecordDefinitionOffset + this.RecordDefinitionCount * (this.FileVersion < 8 ? DataForgeRecordDefinition.RecordSizeInBytes : DataForgeRecordDefinition.RecordSizeInBytesV8); }
		public Int64 Int16ValueOffset { get => this.Int8ValueOffset + this.Int8ValueCount * DataForgeInt8.RecordSizeInBytes; }
		public Int64 Int32ValueOffset { get => this.Int16ValueOffset + this.Int16ValueCount * DataForgeInt16.RecordSizeInBytes; }
		public Int64 Int64ValueOffset { get => this.Int32ValueOffset + this.Int32ValueCount * DataForgeInt32.RecordSizeInBytes; }
		public Int64 UInt8ValueOffset { get => this.Int64ValueOffset + this.Int64ValueCount * DataForgeInt64.RecordSizeInBytes; }
		public Int64 UInt16ValueOffset { get => this.UInt8ValueOffset + this.UInt8ValueCount * DataForgeUInt8.RecordSizeInBytes; }
		public Int64 UInt32ValueOffset { get => this.UInt16ValueOffset + this.UInt16ValueCount * DataForgeUInt16.RecordSizeInBytes; }
		public Int64 UInt64ValueOffset { get => this.UInt32ValueOffset + this.UInt32ValueCount * DataForgeUInt32.RecordSizeInBytes; }
		public Int64 BooleanValueOffset { get => this.UInt64ValueOffset + this.UInt64ValueCount * DataForgeUInt64.RecordSizeInBytes; }
		public Int64 SingleValueOffset { get => this.BooleanValueOffset + this.BooleanValueCount * DataForgeBoolean.RecordSizeInBytes; }
		public Int64 DoubleValueOffset { get => this.SingleValueOffset + this.SingleValueCount * DataForgeSingle.RecordSizeInBytes; }
		public Int64 GuidValueOffset { get => this.DoubleValueOffset + this.DoubleValueCount * DataForgeDouble.RecordSizeInBytes; }
		public Int64 StringValueOffset { get => this.GuidValueOffset + this.GuidValueCount * DataForgeGuid.RecordSizeInBytes; }
		public Int64 LocaleValueOffset { get => this.StringValueOffset + this.StringValueCount * DataForgeStringLookup.RecordSizeInBytes; }
		public Int64 EnumValueOffset { get => this.LocaleValueOffset + this.LocaleValueCount * DataForgeLocale.RecordSizeInBytes; }
		public Int64 StrongValueOffset { get => this.EnumValueOffset + this.EnumValueCount * DataForgeEnum.RecordSizeInBytes; }
		public Int64 WeakValueOffset { get => this.StrongValueOffset + this.StrongValueCount * DataForgePointer.RecordSizeInBytes; }
		public Int64 ReferenceValueOffset { get => this.WeakValueOffset + this.WeakValueCount * DataForgePointer.RecordSizeInBytes; }
		public Int64 EnumOptionOffset { get => this.ReferenceValueOffset + this.ReferenceValueCount * DataForgeReference.RecordSizeInBytes; }
		public Int64 TextOffset { get => this.EnumOptionOffset + this.EnumOptionCount * DataForgeStringLookup.RecordSizeInBytes; }
		public Int64 BlobOffset { get => this.TextOffset + this.TextLength; }
		public Int64 DataOffset { get => this.BlobOffset + this.BlobLength; }

		// Copy any stream to a MemoryStream so all data section seeks and reads
		// are pure in-memory operations — eliminating kernel I/O inside the lock
		// in ReadRecordByPathAsXml and also fixing a latent UAF in unp4k.fs where
		// the caller-owned MemoryStream could be disposed before lazy reads fired.
		private static MemoryStream WrapInMemoryStream(Stream stream)
		{
			if (stream.CanSeek) stream.Seek(0, SeekOrigin.Begin);
			var capacity = stream.CanSeek && stream.Length <= Int32.MaxValue ? (Int32)stream.Length : 0;
			var ms = capacity > 0 ? new MemoryStream(capacity) : new MemoryStream();
			stream.CopyTo(ms);
			ms.Seek(0, SeekOrigin.Begin);
			return ms;
		}

		// Fork constructor: shares all pre-loaded read-only state with the source
		// but wraps the same byte buffer in a new MemoryStream so each fork has its
		// own stream position, RecordStack, and StructStack.  Zero heap allocation
		// beyond the MemoryStream header and the two small guard sets.
		private DataForge(DataForge src, MemoryStream ownStream) : base(ownStream)
		{
			this.IsLegacy = src.IsLegacy;
			this.FileVersion = src.FileVersion;

			this.StructDefinitionCount  = src.StructDefinitionCount;
			this.PropertyDefinitionCount = src.PropertyDefinitionCount;
			this.EnumDefinitionCount    = src.EnumDefinitionCount;
			this.DataMappingCount       = src.DataMappingCount;
			this.RecordDefinitionCount  = src.RecordDefinitionCount;

			this.BooleanValueCount   = src.BooleanValueCount;
			this.Int8ValueCount      = src.Int8ValueCount;
			this.Int16ValueCount     = src.Int16ValueCount;
			this.Int32ValueCount     = src.Int32ValueCount;
			this.Int64ValueCount     = src.Int64ValueCount;
			this.UInt8ValueCount     = src.UInt8ValueCount;
			this.UInt16ValueCount    = src.UInt16ValueCount;
			this.UInt32ValueCount    = src.UInt32ValueCount;
			this.UInt64ValueCount    = src.UInt64ValueCount;
			this.SingleValueCount    = src.SingleValueCount;
			this.DoubleValueCount    = src.DoubleValueCount;
			this.GuidValueCount      = src.GuidValueCount;
			this.StringValueCount    = src.StringValueCount;
			this.LocaleValueCount    = src.LocaleValueCount;
			this.EnumValueCount      = src.EnumValueCount;
			this.StrongValueCount    = src.StrongValueCount;
			this.WeakValueCount      = src.WeakValueCount;
			this.ReferenceValueCount = src.ReferenceValueCount;
			this.EnumOptionCount     = src.EnumOptionCount;
			this.TextLength          = src.TextLength;
			this.BlobLength          = src.BlobLength;

			// Shared read-only lookup tables (built once in the primary constructor)
			this.PathToRecordMap      = src.PathToRecordMap;
			this.ReferenceToRecordMap = src.ReferenceToRecordMap;
			this.StructToDataOffsetMap = src.StructToDataOffsetMap;
			this._cachedDataOffset    = src._cachedDataOffset;

			// Shared pre-loaded string buffers and value arrays (all read-only after ctor)
			this._textBuffer          = src._textBuffer;
			this._blobBuffer          = src._blobBuffer;
			this._structDefinitions   = src._structDefinitions;
			this._propertyDefinitions = src._propertyDefinitions;
			this._enumDefinitions     = src._enumDefinitions;
			this._dataMappings        = src._dataMappings;
			this._recordDefinitions   = src._recordDefinitions;
			this._booleanValues       = src._booleanValues;
			this._int8Values          = src._int8Values;
			this._int16Values         = src._int16Values;
			this._int32Values         = src._int32Values;
			this._int64Values         = src._int64Values;
			this._uint8Values         = src._uint8Values;
			this._uint16Values        = src._uint16Values;
			this._uint32Values        = src._uint32Values;
			this._uint64Values        = src._uint64Values;
			this._singleValues        = src._singleValues;
			this._doubleValues        = src._doubleValues;
			this._guidValues          = src._guidValues;
			this._stringValues        = src._stringValues;
			this._localeValues        = src._localeValues;
			this._enumValues          = src._enumValues;
			this._strongPointerValues = src._strongPointerValues;
			this._weakPointerValues   = src._weakPointerValues;
			this._referenceValues     = src._referenceValues;
			this._enumOptions         = src._enumOptions;

			// Own recursion-guard stacks — not shared between forks
			this.RecordStack = new HashSet<Int32>();
			this.StructStack = new HashSet<(UInt32, UInt32)>();
		}

		// Creates a zero-copy fork: new stream position + recursion stacks,
		// everything else shared by reference.  Cheap enough to call per-thread
		// (in Save) or per Dokan callback (in unp4k.fs).
		public DataForge Fork()
		{
			var buf = ((MemoryStream)this.BaseStream).GetBuffer();
			var len = (Int32)this.BaseStream.Length;
			return new DataForge(this, new MemoryStream(buf, 0, len, writable: false));
		}

		public DataForge(Stream stream) : base(WrapInMemoryStream(stream))
		{
			this.BaseStream.Seek(0, SeekOrigin.Begin);

			_ = this.ReadUInt16();
			_ = this.ReadUInt16();

			this.FileVersion = this.ReadInt32();

			this.IsLegacy = stream.Length < 0x0e2e00 && this.FileVersion < 6;

			if (!this.IsLegacy)
			{
				_ = this.ReadUInt16();
				_ = this.ReadUInt16();
				_ = this.ReadUInt16();
				_ = this.ReadUInt16();
			}

			this.StructDefinitionCount = this.ReadInt32();
			this.PropertyDefinitionCount = this.ReadInt32();
			this.EnumDefinitionCount = this.ReadInt32();
			this.DataMappingCount = this.ReadInt32();
			this.RecordDefinitionCount = this.ReadInt32();
			this.BooleanValueCount = this.ReadInt32();
			this.Int8ValueCount = this.ReadInt32();
			this.Int16ValueCount = this.ReadInt32();
			this.Int32ValueCount = this.ReadInt32();
			this.Int64ValueCount = this.ReadInt32();
			this.UInt8ValueCount = this.ReadInt32();
			this.UInt16ValueCount = this.ReadInt32();
			this.UInt32ValueCount = this.ReadInt32();
			this.UInt64ValueCount = this.ReadInt32();
			this.SingleValueCount = this.ReadInt32();
			this.DoubleValueCount = this.ReadInt32();
			this.GuidValueCount = this.ReadInt32();
			this.StringValueCount = this.ReadInt32();
			this.LocaleValueCount = this.ReadInt32();
			this.EnumValueCount = this.ReadInt32();
			this.StrongValueCount = this.ReadInt32();
			this.WeakValueCount = this.ReadInt32();
			this.ReferenceValueCount = this.ReadInt32();
			this.EnumOptionCount = this.ReadInt32();

			this.TextLength = this.ReadUInt32();
			this.BlobLength = this.IsLegacy ? 0 : this.ReadUInt32();

			this.PathToRecordMap      = new Dictionary<String, Int32>(this.RecordDefinitionCount);
			this.ReferenceToRecordMap = new Dictionary<Guid,   Int32>(this.RecordDefinitionCount);

			// this.ReportOffsets();

			// Pre-load string tables, definition tables and value arrays once
			// from the file. After this point, every per-record read is an
			// array lookup (no stream seek+read+restore per element).
			this.PreloadStringTables();
			this.PreloadDefinitionTables();
			this.PreloadValueArrays();

			for (Int32 recordIndex = 0; recordIndex < this.RecordDefinitionCount; recordIndex++)
			{
				var record = this._recordDefinitions[recordIndex];
				this.PathToRecordMap[record.FileName] = recordIndex;
				this.ReferenceToRecordMap[record.Hash] = recordIndex;
			}

			var lastOffset = 0;

			this.StructToDataOffsetMap = new Dictionary<UInt32, Int64>(this.DataMappingCount);
			for (Int32 dataMappingIndex = 0; dataMappingIndex < this.DataMappingCount; dataMappingIndex++)
			{
				var dataMapping = this._dataMappings[dataMappingIndex];
				var dataStruct = this._structDefinitions[dataMappingIndex];

				if (!this.StructToDataOffsetMap.ContainsKey(dataMapping.StructIndex)) this.StructToDataOffsetMap[dataMapping.StructIndex] = lastOffset;
				lastOffset += (Int32)(dataMapping.StructCount * dataStruct.RecordSize);
			}

			this._cachedDataOffset = this.DataOffset;
			Debug.Assert((this._cachedDataOffset + lastOffset) == this.BaseStream.Length, "Data / Stream length mismatch");
		}

		/// <summary>
		/// Converts Paths to RecordIndexes
		/// </summary>
		public Dictionary<String, Int32> PathToRecordMap { get; }

		/// <summary>
		/// Converts References to RecordIndexes
		/// </summary>
		public Dictionary<Guid, Int32> ReferenceToRecordMap { get; }

		/// <summary>
		/// Converts StructIndex and VariantIndex to dataOffsets
		/// </summary>
		public Dictionary<UInt32, Int64> StructToDataOffsetMap { get; }

		private Int64 _cachedDataOffset;

		// Pre-loaded string tables — read once at ctor time so per-record
		// string lookups become byte-buffer scans instead of stream seeks.
		private Byte[] _textBuffer;
		private Byte[] _blobBuffer;

		// Pre-loaded definition tables — read once at ctor time so per-record
		// reads become array lookups instead of stream seek+read+restore.
		private DataForgeStructDefinition[] _structDefinitions;
		private DataForgePropertyDefinition[] _propertyDefinitions;
		private DataForgeEnumDefinition[] _enumDefinitions;
		private DataForgeDataMapping[] _dataMappings;
		private DataForgeRecordDefinition[] _recordDefinitions;

		// Pre-loaded value arrays — same rationale.
		private DataForgeBoolean[] _booleanValues;
		private DataForgeInt8[] _int8Values;
		private DataForgeInt16[] _int16Values;
		private DataForgeInt32[] _int32Values;
		private DataForgeInt64[] _int64Values;
		private DataForgeUInt8[] _uint8Values;
		private DataForgeUInt16[] _uint16Values;
		private DataForgeUInt32[] _uint32Values;
		private DataForgeUInt64[] _uint64Values;
		private DataForgeSingle[] _singleValues;
		private DataForgeDouble[] _doubleValues;
		private DataForgeGuid[] _guidValues;
		private DataForgeStringLookup[] _stringValues;
		private DataForgeLocale[] _localeValues;
		private DataForgeEnum[] _enumValues;
		private DataForgePointer[] _strongPointerValues;
		private DataForgePointer[] _weakPointerValues;
		private DataForgeReference[] _referenceValues;
		private DataForgeEnum[] _enumOptions;

		private void PreloadStringTables()
		{
			this.Position = this.TextOffset;
			this._textBuffer = new Byte[this.TextLength];
			ReadFully(this.BaseStream, this._textBuffer);

			if (!this.IsLegacy && this.BlobLength > 0)
			{
				this.Position = this.BlobOffset;
				this._blobBuffer = new Byte[this.BlobLength];
				ReadFully(this.BaseStream, this._blobBuffer);
			}
			else
			{
				// Legacy files reuse the text table as their blob table.
				this._blobBuffer = this._textBuffer;
			}
		}

		// Stream.ReadExactly is .NET 7+. We multi-target net6.0 so use a
		// portable loop. FileStream typically returns the full buffer in a
		// single Read() for hot files, so this is one iteration in practice.
		private static void ReadFully(Stream stream, Byte[] buffer)
		{
			Int32 total = 0;
			while (total < buffer.Length)
			{
				Int32 read = stream.Read(buffer, total, buffer.Length - total);
				if (read == 0) throw new EndOfStreamException("Unexpected end of stream while preloading.");
				total += read;
			}
		}

		private void PreloadDefinitionTables()
		{
			this.Position = this.StructDefinitionOffset;
			this._structDefinitions = new DataForgeStructDefinition[this.StructDefinitionCount];
			for (Int32 i = 0; i < this.StructDefinitionCount; i++) this._structDefinitions[i] = DataForgeStructDefinition.ReadFromStream(this);

			this.Position = this.PropertyDefinitionOffset;
			this._propertyDefinitions = new DataForgePropertyDefinition[this.PropertyDefinitionCount];
			for (Int32 i = 0; i < this.PropertyDefinitionCount; i++) this._propertyDefinitions[i] = DataForgePropertyDefinition.ReadFromStream(this);

			this.Position = this.EnumDefinitionOffset;
			this._enumDefinitions = new DataForgeEnumDefinition[this.EnumDefinitionCount];
			for (Int32 i = 0; i < this.EnumDefinitionCount; i++) this._enumDefinitions[i] = DataForgeEnumDefinition.ReadFromStream(this);

			this.Position = this.DataMappingOffset;
			this._dataMappings = new DataForgeDataMapping[this.DataMappingCount];
			for (Int32 i = 0; i < this.DataMappingCount; i++) this._dataMappings[i] = DataForgeDataMapping.ReadFromStream(this);

			this.Position = this.RecordDefinitionOffset;
			this._recordDefinitions = new DataForgeRecordDefinition[this.RecordDefinitionCount];
			for (Int32 i = 0; i < this.RecordDefinitionCount; i++) this._recordDefinitions[i] = DataForgeRecordDefinition.ReadFromStream(this);
		}

		private void PreloadValueArrays()
		{
			this.Position = this.Int8ValueOffset;
			this._int8Values = new DataForgeInt8[this.Int8ValueCount];
			for (Int32 i = 0; i < this.Int8ValueCount; i++) this._int8Values[i] = DataForgeInt8.ReadFromStream(this);

			this.Position = this.Int16ValueOffset;
			this._int16Values = new DataForgeInt16[this.Int16ValueCount];
			for (Int32 i = 0; i < this.Int16ValueCount; i++) this._int16Values[i] = DataForgeInt16.ReadFromStream(this);

			this.Position = this.Int32ValueOffset;
			this._int32Values = new DataForgeInt32[this.Int32ValueCount];
			for (Int32 i = 0; i < this.Int32ValueCount; i++) this._int32Values[i] = DataForgeInt32.ReadFromStream(this);

			this.Position = this.Int64ValueOffset;
			this._int64Values = new DataForgeInt64[this.Int64ValueCount];
			for (Int32 i = 0; i < this.Int64ValueCount; i++) this._int64Values[i] = DataForgeInt64.ReadFromStream(this);

			this.Position = this.UInt8ValueOffset;
			this._uint8Values = new DataForgeUInt8[this.UInt8ValueCount];
			for (Int32 i = 0; i < this.UInt8ValueCount; i++) this._uint8Values[i] = DataForgeUInt8.ReadFromStream(this);

			this.Position = this.UInt16ValueOffset;
			this._uint16Values = new DataForgeUInt16[this.UInt16ValueCount];
			for (Int32 i = 0; i < this.UInt16ValueCount; i++) this._uint16Values[i] = DataForgeUInt16.ReadFromStream(this);

			this.Position = this.UInt32ValueOffset;
			this._uint32Values = new DataForgeUInt32[this.UInt32ValueCount];
			for (Int32 i = 0; i < this.UInt32ValueCount; i++) this._uint32Values[i] = DataForgeUInt32.ReadFromStream(this);

			this.Position = this.UInt64ValueOffset;
			this._uint64Values = new DataForgeUInt64[this.UInt64ValueCount];
			for (Int32 i = 0; i < this.UInt64ValueCount; i++) this._uint64Values[i] = DataForgeUInt64.ReadFromStream(this);

			this.Position = this.BooleanValueOffset;
			this._booleanValues = new DataForgeBoolean[this.BooleanValueCount];
			for (Int32 i = 0; i < this.BooleanValueCount; i++) this._booleanValues[i] = DataForgeBoolean.ReadFromStream(this);

			this.Position = this.SingleValueOffset;
			this._singleValues = new DataForgeSingle[this.SingleValueCount];
			for (Int32 i = 0; i < this.SingleValueCount; i++) this._singleValues[i] = DataForgeSingle.ReadFromStream(this);

			this.Position = this.DoubleValueOffset;
			this._doubleValues = new DataForgeDouble[this.DoubleValueCount];
			for (Int32 i = 0; i < this.DoubleValueCount; i++) this._doubleValues[i] = DataForgeDouble.ReadFromStream(this);

			this.Position = this.GuidValueOffset;
			this._guidValues = new DataForgeGuid[this.GuidValueCount];
			for (Int32 i = 0; i < this.GuidValueCount; i++) this._guidValues[i] = DataForgeGuid.ReadFromStream(this);

			this.Position = this.StringValueOffset;
			this._stringValues = new DataForgeStringLookup[this.StringValueCount];
			for (Int32 i = 0; i < this.StringValueCount; i++) this._stringValues[i] = DataForgeStringLookup.ReadFromStream(this);

			this.Position = this.LocaleValueOffset;
			this._localeValues = new DataForgeLocale[this.LocaleValueCount];
			for (Int32 i = 0; i < this.LocaleValueCount; i++) this._localeValues[i] = DataForgeLocale.ReadFromStream(this);

			this.Position = this.EnumValueOffset;
			this._enumValues = new DataForgeEnum[this.EnumValueCount];
			for (Int32 i = 0; i < this.EnumValueCount; i++) this._enumValues[i] = DataForgeEnum.ReadFromStream(this);

			this.Position = this.StrongValueOffset;
			this._strongPointerValues = new DataForgePointer[this.StrongValueCount];
			for (Int32 i = 0; i < this.StrongValueCount; i++) this._strongPointerValues[i] = DataForgePointer.ReadFromStream(this);

			this.Position = this.WeakValueOffset;
			this._weakPointerValues = new DataForgePointer[this.WeakValueCount];
			for (Int32 i = 0; i < this.WeakValueCount; i++) this._weakPointerValues[i] = DataForgePointer.ReadFromStream(this);

			this.Position = this.ReferenceValueOffset;
			this._referenceValues = new DataForgeReference[this.ReferenceValueCount];
			for (Int32 i = 0; i < this.ReferenceValueCount; i++) this._referenceValues[i] = DataForgeReference.ReadFromStream(this);

			this.Position = this.EnumOptionOffset;
			this._enumOptions = new DataForgeEnum[this.EnumOptionCount];
			for (Int32 i = 0; i < this.EnumOptionCount; i++) this._enumOptions[i] = DataForgeEnum.ReadFromStream(this);
		}

		internal String ReadEnumAtOffset(UInt32 enumValueOffset) => this.ReadTextAtOffset(enumValueOffset);

		internal String ReadTextAtOffset(Int64 offset)
		{
			if (offset > this.TextLength) throw new IndexOutOfRangeException($"Offset {offset} is out of range for Text values (length: {this.TextLength})");
			return ReadCStringFrom(this._textBuffer, (Int32)offset);
		}

		internal String ReadBlobAtOffset(Int64 offset)
		{
			if (this.FileVersion < 6) return this.ReadTextAtOffset(offset);
			if (offset > this.BlobLength) throw new IndexOutOfRangeException($"Offset {offset} is out of range for Blob values (length: {this.BlobLength})");
			return ReadCStringFrom(this._blobBuffer, (Int32)offset);
		}

		// Scan a null-terminated string out of the pre-loaded text/blob buffer.
		// Matches the original byte→char cast (Latin-1) so string output is
		// byte-identical to the prior ReadCString() path.
		private static String ReadCStringFrom(Byte[] buffer, Int32 offset)
		{
			Int32 end = offset;
			while (end < buffer.Length && buffer[end] != 0) end++;
			Int32 length = end - offset;
			if (length == 0) return String.Empty;
			return Encoding.Latin1.GetString(buffer, offset, length);
		}

		internal DataForgeDataMapping ReadDataMappingAtIndex(Int64 index) => this._dataMappings[index];

		internal DataForgeRecordDefinition ReadRecordDefinitionAtIndex(Int64 index) => this._recordDefinitions[index];

		public HashSet<(UInt32, UInt32)> StructStack { get; set; } = new HashSet<(UInt32, UInt32)>();

		public XmlElement ReadStructAtIndexAsXml(XmlElement xmlNode, UInt32 structIndex, UInt32 variantIndex)
		{
			var position = this.Position;

			if (this.StructStack.Count > DataForge.MaxPointerDepth || this.StructStack.Contains((structIndex, variantIndex))) return null;

			try
			{
				this.StructStack.Add((structIndex, variantIndex));

				var dataStruct = this.ReadStructDefinitionAtIndex(structIndex);
				var dataMapping = this.ReadDataMappingAtIndex(structIndex);

				if (dataMapping.StructCount < variantIndex) throw new IndexOutOfRangeException($"Variant Index {variantIndex} is out of range for struct {dataStruct.Name} with count {dataMapping.StructCount}");

				if (this.StructToDataOffsetMap.TryGetValue(structIndex, out long value))
				{
					this.Position = this._cachedDataOffset + value + (dataStruct.RecordSize * variantIndex);
				}
				else
				{
					throw new KeyNotFoundException($"Struct Index {structIndex} not found in Struct to Data Offset Map");
					// this.Position = this.DataOffset;
				}

				return this.ReadStructAsXml(xmlNode, dataStruct);
			}
			finally
			{
				this.Position = position;

				this.StructStack.Remove((structIndex, variantIndex));
			}
		}

		public XmlElement ReadStructAsXml(XmlElement xmlNode, DataForgeStructDefinition dataStruct)
		{
			var i = 0;

			foreach (var childNode in dataStruct.ReadAsXml(xmlNode))
			{
				if (childNode == null) continue;
				if (childNode is XmlAttribute attribute) xmlNode.Attributes.Append(attribute);
				else if (childNode is XmlElement element) xmlNode.AppendChild(element);

				if (++i >= DataForge.MaxNodes) break;
			}

			if (xmlNode.ChildNodes.Count == 0 && xmlNode.Attributes.Count == 0) return null;

			return xmlNode;
		}

		internal DataForgeStructDefinition ReadStructDefinitionAtIndex(Int64 index) => this._structDefinitions[index];

		internal DataForgePropertyDefinition ReadPropertyDefinitionAtIndex(Int64 index) => this._propertyDefinitions[index];

		internal DataForgeBoolean ReadBooleanAtIndex(Int64 index) => this._booleanValues[index];

		internal DataForgeDouble ReadDoubleAtIndex(Int64 index) => this._doubleValues[index];

		internal DataForgeEnum ReadEnumOptionAtIndex(Int64 index) => this._enumOptions[index];

		internal DataForgeEnumDefinition ReadEnumDefinitionAtIndex(Int64 index) => this._enumDefinitions[index];

		internal DataForgeEnum ReadEnumValueAtIndex(Int64 index) => this._enumValues[index];

		internal DataForgeGuid ReadGuidAtIndex(Int64 index) => this._guidValues[index];

		internal DataForgeInt16 ReadInt16AtIndex(Int64 index) => this._int16Values[index];

		internal DataForgeInt32 ReadInt32AtIndex(Int64 index) => this._int32Values[index];

		internal DataForgeInt64 ReadInt64AtIndex(Int64 index) => this._int64Values[index];

		internal DataForgeInt8 ReadInt8AtIndex(Int64 index) => this._int8Values[index];

		internal DataForgeLocale ReadLocaleAtIndex(Int64 index) => this._localeValues[index];

		internal DataForgeReference ReadReferenceAtIndex(Int64 index) => this._referenceValues[index];

		internal DataForgeSingle ReadSingleAtIndex(Int64 index) => this._singleValues[index];

		internal DataForgeStringLookup ReadStringAtIndex(Int64 index) => this._stringValues[index];

		internal DataForgeUInt16 ReadUInt16AtIndex(Int64 index) => this._uint16Values[index];

		internal DataForgeUInt32 ReadUInt32AtIndex(Int64 index) => this._uint32Values[index];

		internal DataForgeUInt64 ReadUInt64AtIndex(Int64 index) => this._uint64Values[index];

		internal DataForgeUInt8 ReadUInt8AtIndex(Int64 index) => this._uint8Values[index];

		internal DataForgePointer ReadWeakPointerAtIndex(Int64 index) => this._weakPointerValues[index];

		internal DataForgePointer ReadStrongPointerAtIndex(Int64 index) => this._strongPointerValues[index];

		public XmlElement ReadRecordByPathAsXml(String path)
		{
			if (!this.PathToRecordMap.TryGetValue(path, out var recordIndex)) throw new FileNotFoundException();

			var xml = new XmlDocument();
			return this.Fork().ReadRecordAtIndexAsXml(xml, recordIndex);
		}
        
		

		public XmlElement ReadRecordByReferenceAsXml(XmlNode xmlNode, Guid reference)
		{
			if (!this.ReferenceToRecordMap.TryGetValue(reference, out var recordIndex)) throw new FileNotFoundException();

			return this.ReadRecordAtIndexAsXml(xmlNode, recordIndex);
		}

		public HashSet<Int32> RecordStack { get; set; } = new HashSet<Int32>();

		public XmlElement ReadRecordAtIndexAsXml(XmlNode xmlNode, Int32 recordIndex)
		{
			var position = this.Position;

			if (this.RecordStack.Count > DataForge.MaxReferenceDepth || this.RecordStack.Contains(recordIndex)) return null;

			try
			{
				this.RecordStack.Add(recordIndex);

				var record = this.ReadRecordDefinitionAtIndex(recordIndex);

				return record.ReadAsXml(xmlNode);
			}
			catch (Exception ex)
			{
				return xmlNode.CreateElementWithValue("Error", ex.Message);
			}
			finally
			{
				this.Position = position;

				this.RecordStack.Remove(recordIndex);
			}
		}

		private void ReportOffsets()
		{
			Console.WriteLine($"StructDefinitionOffset:   {this.StructDefinitionOffset:X8}\t{this.StructDefinitionCount:X6}");
			Console.WriteLine($"PropertyDefinitionOffset: {this.PropertyDefinitionOffset:X8}\t{this.PropertyDefinitionCount:X6}");
			Console.WriteLine($"EnumDefinitionOffset:     {this.EnumDefinitionOffset:X8}\t{this.EnumDefinitionCount:X6}");
			Console.WriteLine($"DataMappingOffset:        {this.DataMappingOffset:X8}\t{this.DataMappingCount:X6}");
			Console.WriteLine($"RecordDefinitionOffset:   {this.RecordDefinitionOffset:X8}\t{this.RecordDefinitionCount:X6}");
			Console.WriteLine($"Int8ValueOffset:          {this.Int8ValueOffset:X8}\t{this.Int8ValueCount:X6}");
			Console.WriteLine($"Int16ValueOffset:         {this.Int16ValueOffset:X8}\t{this.Int16ValueCount:X6}");
			Console.WriteLine($"Int32ValueOffset:         {this.Int32ValueOffset:X8}\t{this.Int32ValueCount:X6}");
			Console.WriteLine($"Int64ValueOffset:         {this.Int64ValueOffset:X8}\t{this.Int64ValueCount:X6}");
			Console.WriteLine($"UInt8ValueOffset:         {this.UInt8ValueOffset:X8}\t{this.UInt8ValueCount:X6}");
			Console.WriteLine($"UInt16ValueOffset:        {this.UInt16ValueOffset:X8}\t{this.UInt16ValueCount:X6}");
			Console.WriteLine($"UInt32ValueOffset:        {this.UInt32ValueOffset:X8}\t{this.UInt32ValueCount:X6}");
			Console.WriteLine($"UInt64ValueOffset:        {this.UInt64ValueOffset:X8}\t{this.UInt64ValueCount:X6}");
			Console.WriteLine($"BooleanValueOffset:       {this.BooleanValueOffset:X8}\t{this.BooleanValueCount:X6}");
			Console.WriteLine($"SingleValueOffset:        {this.SingleValueOffset:X8}\t{this.SingleValueCount:X6}");
			Console.WriteLine($"DoubleValueOffset:        {this.DoubleValueOffset:X8}\t{this.DoubleValueCount:X6}");
			Console.WriteLine($"GuidValueOffset:          {this.GuidValueOffset:X8}\t{this.GuidValueCount:X6}");
			Console.WriteLine($"StringValueOffset:        {this.StringValueOffset:X8}\t{this.StringValueCount:X6}");
			Console.WriteLine($"LocaleValueOffset:        {this.LocaleValueOffset:X8}\t{this.LocaleValueCount:X6}");
			Console.WriteLine($"EnumValueOffset:          {this.EnumValueOffset:X8}\t{this.EnumValueCount:X6}");
			Console.WriteLine($"StrongValueOffset:        {this.StrongValueOffset:X8}\t{this.StringValueCount:X6}");
			Console.WriteLine($"WeakValueOffset:          {this.WeakValueOffset:X8}\t{this.WeakValueCount:X6}");
			Console.WriteLine($"ReferenceValueOffset:     {this.ReferenceValueOffset:X8}\t{this.ReferenceValueCount:X6}");
			Console.WriteLine($"EnumOptionOffset:         {this.EnumOptionOffset:X8}\t{this.EnumOptionCount:X6}");
			Console.WriteLine($"TextOffset:               {this.TextOffset:X8}");
			Console.WriteLine($"BlobOffset:               {this.BlobOffset:X8}");
			Console.WriteLine($"DataOffset:               {this.DataOffset:X8}");
			Console.WriteLine($"Length:                   {this.Length:X8}");
		}

		private static readonly XmlWriterSettings _xmlSettings = new XmlWriterSettings
		{
			OmitXmlDeclaration = true,
			Encoding = new UTF8Encoding(false), // UTF-8, no BOM
			Indent = true,
			IndentChars = "  ",
			NewLineChars = "\r\n",
			NewLineHandling = NewLineHandling.Replace,
			ConformanceLevel = ConformanceLevel.Document,
			CheckCharacters = false
		};

		public void Save(String filename)
		{
			var totalSw = Stopwatch.StartNew();
			var baseDir = Path.GetDirectoryName(Path.GetFullPath(filename)) ?? ".";
			var written = 0;
			var skipped = 0;
			Int64 bytesWritten = 0;

			// Track directories already created to avoid redundant syscalls.
			var createdDirs = new System.Collections.Concurrent.ConcurrentDictionary<String, Boolean>(StringComparer.OrdinalIgnoreCase);

			// Each thread owns a forked DataForge with its own stream position and
			// recursion stacks, so reads are fully parallel — no shared lock needed.
			Parallel.ForEach(
				this.PathToRecordMap.Keys,
				new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
				() => this.Fork(),
				(fileReference, _, localReader) =>
				{
					var node = localReader.ReadRecordByPathAsXml(fileReference);
					if (node == null) { Interlocked.Increment(ref skipped); return localReader; }

					var newPath = Path.Combine(baseDir, fileReference);
					var dir = Path.GetDirectoryName(newPath);
					if (!String.IsNullOrEmpty(dir))
						createdDirs.GetOrAdd(dir, static d => { Directory.CreateDirectory(d); return true; });

					using (var fileStream = File.Create(newPath))
					using (var writer = XmlWriter.Create(fileStream, _xmlSettings))
					{
						node.WriteTo(writer);
						writer.Flush();
						Interlocked.Add(ref bytesWritten, fileStream.Length);
					}
					Interlocked.Increment(ref written);
					return localReader;
				},
				localReader => localReader.Dispose()
			);

			totalSw.Stop();

			Console.WriteLine();
			Console.WriteLine("=== unforge Save() timing ===");
			Console.WriteLine($"  records:    {written} written, {skipped} skipped (null)");
			Console.WriteLine($"  output:     {bytesWritten / 1_048_576.0,8:F1} MB");
			Console.WriteLine($"  total:      {totalSw.Elapsed.TotalSeconds,8:F2}s");
		}
	}
}
