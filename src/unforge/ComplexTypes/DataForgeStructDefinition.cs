using Dolkens.Framework.BinaryExtensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;

namespace unforge
{
	public class DataForgeStructDefinition : DataForgeTypeReader
	{
		public static Int32 RecordSizeInBytes = 16;

		public UInt32 NameOffset { get; }
		private String _name;
		public String Name { get => _name ??= this.StreamReader.ReadBlobAtOffset(this.NameOffset); }

        public UInt32 ParentTypeIndex { get; }

		public DataForgeStructDefinition ParentType
		{
			get
			{
				if (this.ParentTypeIndex == 0xFFFFFFFF) return null;
				
				return this.StreamReader.ReadStructDefinitionAtIndex(this.ParentTypeIndex);
			}
		}


        public UInt16 PropertyCount { get; }

        public UInt16 FirstPropertyIndex { get; }

        public UInt32 RecordSize { get; }
		

		public static DataForgeStructDefinition ReadFromStream(DataForge baseStream) => new DataForgeStructDefinition(baseStream);

		private DataForgeStructDefinition(DataForge reader) : base(reader)
		{
			this.NameOffset = reader.ReadUInt32();
			this.ParentTypeIndex = reader.ReadUInt32();
			this.PropertyCount = reader.ReadUInt16();
			this.FirstPropertyIndex = reader.ReadUInt16();
			this.RecordSize = reader.ReadUInt32();
		}

		private DataForgeStructDefinition[] _hierarchy;
		public DataForgeStructDefinition[] Hierarchy
		{
			get
			{
				if (_hierarchy != null) return _hierarchy;
				var list = new List<DataForgeStructDefinition>();
				if (this.ParentTypeIndex != 0xFFFFFFFF)
					list.AddRange(this.ParentType.Hierarchy);
				list.Add(this);
				return _hierarchy = list.ToArray();
			}
		}

		private DataForgePropertyDefinition[] _propertyDefinitions;
		public DataForgePropertyDefinition[] PropertyDefinitions
		{
			get
			{
				if (_propertyDefinitions != null) return _propertyDefinitions;
				var result = new List<DataForgePropertyDefinition>();
				foreach (var s in this.Hierarchy)
					for (var i = s.FirstPropertyIndex; i < s.FirstPropertyIndex + s.PropertyCount; i++)
						result.Add(this.StreamReader.ReadPropertyDefinitionAtIndex(i));
				return _propertyDefinitions = result.ToArray();
			}
		}

		public IEnumerable<XmlNode> ReadAsXml(DataForge reader, XmlNode parentNode)
		{
			var i = 0;

			foreach (var propertyDefinition in this.PropertyDefinitions)
			{
				if (propertyDefinition.ConversionType == EConversionType.varAttribute) yield return this.ReadValueAsXml(reader, parentNode, propertyDefinition);
				else
				{
					var xmlNode = parentNode.OwnerDocument.CreateElement(propertyDefinition.Name);

					foreach (var childNode in this.ReadArrayAsXml(reader, xmlNode, propertyDefinition))
					{
								if (childNode == null) continue;
						xmlNode.AppendChild(childNode);
					}

					if (xmlNode.ChildNodes.Count == 0 && xmlNode.Attributes.Count == 0) continue;

					yield return xmlNode;
				}

				if (++i >= DataForge.MaxNodes) yield break;
			}
		}

		public XmlNode ReadValueAsXml(DataForge reader, XmlNode parentNode, DataForgePropertyDefinition propertyDefinition, String nameOverride = null)
		{
			try
			{
				switch (propertyDefinition.DataType)
				{
					case EDataType.varClass:
						{
							var dataStruct = reader.ReadStructDefinitionAtIndex(propertyDefinition.Index);

							var xmlNode = parentNode.OwnerDocument.CreateElement(nameOverride ?? propertyDefinition.Name);

							foreach (var childNode in dataStruct.ReadAsXml(reader, xmlNode))
							{
								if (childNode == null) continue;
								if (childNode is XmlAttribute attribute) xmlNode.Attributes.Append(attribute);
								else if (childNode is XmlElement element) xmlNode.AppendChild(element);
							}

							if (xmlNode == null) return null;

							if (xmlNode.ChildNodes.Count == 0 && xmlNode.Attributes.Count == 0) return null;

							return xmlNode;
						}
					case EDataType.varReference:
						{
							var dataForgeReference = DataForgeReference.ReadFromStream(reader);

							if (dataForgeReference.IsNull) return null;

							if (!reader.FollowReferences) return parentNode.CreateAttributeWithValue(nameOverride ?? propertyDefinition.Name, dataForgeReference.Value);
							
							var xmlNode = reader.ReadRecordByReferenceAsXml(parentNode, dataForgeReference.Value);

							if (xmlNode == null) return parentNode.CreateAttributeWithValue(nameOverride ?? propertyDefinition.Name, dataForgeReference.Value);

							if (xmlNode.ChildNodes.Count == 0 && xmlNode.Attributes.Count == 0) return null;

							return xmlNode;
						}
					case EDataType.varStrongPointer:
						{
							var pointer = DataForgePointer.ReadFromStream(reader);

							if (pointer.IsNull) return null;

							var dataStruct = reader.ReadStructDefinitionAtIndex(pointer.StructIndex);

							if (dataStruct == null) return null;
							
							if (!reader.FollowStrongPointers) return parentNode.CreateElementWithValue(nameOverride ?? propertyDefinition.Name, String.Format("{1}[{2:X4}]", propertyDefinition.DataType, dataStruct.Name, pointer.VariantIndex, pointer.Padding));

							var xmlNode = reader.ReadStructAtIndexAsXml(parentNode.OwnerDocument.CreateElement(dataStruct.Name), pointer.StructIndex, pointer.VariantIndex);

							if (xmlNode == null) return null;

							if (xmlNode.ChildNodes.Count == 0 && xmlNode.Attributes.Count == 0) return null;

							return parentNode.CreateElementWithValue(nameOverride ?? propertyDefinition.Name, xmlNode);
						}
					case EDataType.varWeakPointer:
						{
							var pointer = DataForgePointer.ReadFromStream(reader);

							if (pointer.IsNull) return null;

							var dataStruct = reader.ReadStructDefinitionAtIndex(pointer.StructIndex);

							return parentNode.CreateElementWithValue(nameOverride ?? propertyDefinition.Name, String.Format("{1}[{2:X4}]", propertyDefinition.DataType, dataStruct.Name, pointer.VariantIndex, pointer.Padding));

							var result = reader.ReadStructAtIndexAsXml(parentNode.OwnerDocument.CreateElement(dataStruct.Name), pointer.StructIndex, pointer.VariantIndex);

							if (result != null) return parentNode.CreateElementWithValue(nameOverride ?? propertyDefinition.Name, result);

							return null;
						}
					case EDataType.varLocale:
						{
							var localeIndex = reader.ReadUInt32();

							return parentNode.CreateAttributeWithValue(nameOverride ?? propertyDefinition.Name, reader.ReadTextAtOffset(localeIndex));
						}
					case EDataType.varString: return parentNode.CreateAttributeWithValue(nameOverride ?? propertyDefinition.Name, reader.ReadTextAtOffset(reader.ReadUInt32()));
					case EDataType.varEnum: return parentNode.CreateAttributeWithValue(nameOverride ?? propertyDefinition.Name, reader.ReadEnumAtOffset(reader.ReadUInt32()));
					case EDataType.varBoolean: return parentNode.CreateAttributeWithValue(nameOverride ?? propertyDefinition.Name, reader.ReadByte());
					case EDataType.varSingle: return parentNode.CreateAttributeWithValue(nameOverride ?? propertyDefinition.Name, reader.ReadSingle());
					case EDataType.varDouble: return parentNode.CreateAttributeWithValue(nameOverride ?? propertyDefinition.Name, reader.ReadDouble());
					case EDataType.varGuid: return parentNode.CreateAttributeWithValue(nameOverride ?? propertyDefinition.Name, reader.ReadGuid(false));
					case EDataType.varInt8: return parentNode.CreateAttributeWithValue(nameOverride ?? propertyDefinition.Name, reader.ReadSByte());
					case EDataType.varInt16: return parentNode.CreateAttributeWithValue(nameOverride ?? propertyDefinition.Name, reader.ReadInt16());
					case EDataType.varInt32: return parentNode.CreateAttributeWithValue(nameOverride ?? propertyDefinition.Name, reader.ReadInt32());
					case EDataType.varInt64: return parentNode.CreateAttributeWithValue(nameOverride ?? propertyDefinition.Name, reader.ReadInt64());
					case EDataType.varUInt8: return parentNode.CreateAttributeWithValue(nameOverride ?? propertyDefinition.Name, reader.ReadByte());
					case EDataType.varUInt16: return parentNode.CreateAttributeWithValue(nameOverride ?? propertyDefinition.Name, reader.ReadUInt16());
					case EDataType.varUInt32: return parentNode.CreateAttributeWithValue(nameOverride ?? propertyDefinition.Name, reader.ReadUInt32());
					case EDataType.varUInt64: return parentNode.CreateAttributeWithValue(nameOverride ?? propertyDefinition.Name, reader.ReadUInt64());
				}
			}
			catch (Exception ex)
			{
				return parentNode.CreateAttributeWithValue(nameOverride ?? propertyDefinition.Name, $"Error reading property {propertyDefinition.Name} of type {propertyDefinition.DataType}: {ex}");
			}

			return parentNode.CreateAttributeWithValue(nameOverride ?? propertyDefinition.Name, $"Unhandled Type {propertyDefinition.DataType}");
		}

		public IEnumerable<XmlElement> ReadArrayAsXml(DataForge reader, XmlNode parentNode, DataForgePropertyDefinition propertyDefinition)
		{
			var arrayCount = reader.ReadUInt32();
			var firstIndex = reader.ReadUInt32();

			for (UInt16 i = 0; i < arrayCount; i++)
			{
				yield return this.ReadArrayValueAsXml(reader, parentNode, propertyDefinition, firstIndex, i);
				
				if (i >= (DataForge.MaxNodes - 1)) break;
			}
		}

		public XmlElement ReadArrayValueAsXml(DataForge reader, XmlNode parentNode, DataForgePropertyDefinition propertyDefinition, UInt32 firstIndex, UInt16 offset)
		{
			try
			{
				switch (propertyDefinition.DataType)
				{
					case EDataType.varBoolean: return parentNode.CreateElementWithValue($"Bool", reader.ReadBooleanAtIndex(firstIndex + offset).Value ? 1 : 0);

					case EDataType.varSingle: return parentNode.CreateElementWithValue($"Single", reader.ReadSingleAtIndex(firstIndex + offset).Value);
					case EDataType.varDouble: return parentNode.CreateElementWithValue($"Double", reader.ReadDoubleAtIndex(firstIndex + offset).Value);

					case EDataType.varGuid: return parentNode.CreateElementWithValue($"Guid", reader.ReadGuidAtIndex(firstIndex + offset).Value);
					case EDataType.varReference:

						if (reader.FollowReferences)
						{
							var dataForgeReference = reader.ReadReferenceAtIndex(firstIndex + offset);
							var xmlNode = reader.ReadRecordByReferenceAsXml(parentNode, dataForgeReference.Value);

							if (xmlNode is XmlElement xmlElement) return xmlElement;
						}

						return parentNode.CreateElementWithValue($"Reference", reader.ReadReferenceAtIndex(firstIndex + offset).Value);
				
					case EDataType.varUInt8: return parentNode.CreateElementWithValue($"UInt8", reader.ReadUInt8AtIndex(firstIndex + offset).Value);
					case EDataType.varUInt16: return parentNode.CreateElementWithValue($"UInt16", reader.ReadUInt16AtIndex(firstIndex + offset).Value);
					case EDataType.varUInt32: return parentNode.CreateElementWithValue($"UInt32", reader.ReadUInt32AtIndex(firstIndex + offset).Value);
					case EDataType.varUInt64: return parentNode.CreateElementWithValue($"UInt64", reader.ReadUInt64AtIndex(firstIndex + offset).Value);
					
					case EDataType.varInt8: return parentNode.CreateElementWithValue($"Int8", reader.ReadInt8AtIndex(firstIndex + offset).Value);
					case EDataType.varInt16: return parentNode.CreateElementWithValue($"Int16", reader.ReadInt16AtIndex(firstIndex + offset).Value);
					case EDataType.varInt32: return parentNode.CreateElementWithValue($"Int32", reader.ReadInt32AtIndex(firstIndex + offset).Value);
					case EDataType.varInt64: return parentNode.CreateElementWithValue($"Int64", reader.ReadInt64AtIndex(firstIndex + offset).Value);
					
					case EDataType.varString: return parentNode.CreateElementWithValue($"String", reader.ReadStringAtIndex(firstIndex + offset).Value);
					case EDataType.varLocale: return parentNode.CreateElementWithValue($"LocID", reader.ReadLocaleAtIndex(firstIndex + offset).Value);
					case EDataType.varEnum: return parentNode.CreateElementWithValue($"Enum", reader.ReadEnumValueAtIndex(firstIndex + offset).Value);

					case EDataType.varWeakPointer:
						{
							var pointer = reader.ReadWeakPointerAtIndex(offset + firstIndex);
							var dataMapping = reader.ReadDataMappingAtIndex(pointer.StructIndex);

							return reader.ReadStructAtIndexAsXml(parentNode.OwnerDocument.CreateElement(dataMapping.Name), pointer.StructIndex, pointer.VariantIndex);
						}
					case EDataType.varStrongPointer:
						{
							var pointer = reader.ReadStrongPointerAtIndex(offset + firstIndex);
							var dataMapping = reader.ReadDataMappingAtIndex(pointer.StructIndex);

							return reader.ReadStructAtIndexAsXml(parentNode.OwnerDocument.CreateElement(dataMapping.Name), pointer.StructIndex, pointer.VariantIndex);
						}
					case EDataType.varClass:
						{
							var structIndex = (UInt32)(propertyDefinition.Index);
							var dataMapping = reader.ReadDataMappingAtIndex(structIndex);

							return reader.ReadStructAtIndexAsXml(parentNode.OwnerDocument.CreateElement(dataMapping.Name), propertyDefinition.Index, firstIndex + offset);
						}
				}
			}
			catch (Exception ex)
			{
				return parentNode.CreateElementWithValue(propertyDefinition.Name, $"Error reading array property {propertyDefinition.Name} of type {propertyDefinition.DataType}: {ex}");
			}

			return parentNode.CreateElementWithValue(propertyDefinition.Name, "TBC");
		}

		public override String ToString()
        {
            return String.Format("<{0} />", this.Name);
        }
    }
}
