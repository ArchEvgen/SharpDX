﻿// Copyright (c) 2010-2012 SharpDX - Alexandre Mutel
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using SharpDX.Direct3D11;
using SharpDX.Serialization;

namespace SharpDX.Toolkit.Graphics
{
    internal class ModelReader : BinarySerializer
    {
        private DataPointer sharedPtr;

        public ModelReader(GraphicsDevice graphicsDevice, Stream stream, ModelMaterialTextureLoaderDelegate textureLoader) : base(stream, SerializerMode.Read, ASCIIEncoding.ASCII)
        {
            if (graphicsDevice == null)
            {
                throw new ArgumentNullException("graphicsDevice");
            }

            if (stream == null)
            {
                throw new ArgumentNullException("stream");
            }

            if (textureLoader == null)
            {
                throw new ArgumentNullException("textureLoader");
            }

            GraphicsDevice = graphicsDevice;
            TextureLoaderDelegate = textureLoader;
            Model = CreateModel();
        }

        internal void AllocateSharedMemory(int size)
        {
            sharedPtr = new DataPointer(Utilities.AllocateMemory(size), size);
            ToDispose(sharedPtr.Pointer);
        }

        internal IntPtr SharedMemoryPointer
        {
            get
            {
                return sharedPtr.Pointer;
            }
        }

        protected readonly Model Model;

        protected ModelMesh CurrentMesh;

        protected readonly GraphicsDevice GraphicsDevice;

        protected readonly ModelMaterialTextureLoaderDelegate TextureLoaderDelegate;

        protected virtual Model CreateModel()
        {
            return new Model();
        }

        protected virtual Material CreateModelMaterial()
        {
            return new Material();
        }

        protected virtual ModelBone CreateModelBone()
        {
            return new ModelBone();
        }

        protected virtual ModelMesh CreateModelMesh()
        {
            return new ModelMesh();
        }

        protected virtual ModelMeshPart CreateModelMeshPart()
        {
            return new ModelMeshPart();
        }

        protected virtual MaterialCollection CreateModelMaterialCollection(int capacity)
        {
            return new MaterialCollection(capacity);
        }

        protected virtual ModelBoneCollection CreateModelBoneCollection(int capacity)
        {
            return new ModelBoneCollection(capacity);
        }

        protected virtual ModelMeshCollection CreateModelMeshCollection(int capacity)
        {
            return new ModelMeshCollection(capacity);
        }

        protected virtual ModelMeshPartCollection CreateModelMeshPartCollection(int capacity)
        {
            return new ModelMeshPartCollection(capacity);
        }

        protected virtual VertexBufferBindingCollection CreateVertexBufferBindingCollection(int capacity)
        {
            return new VertexBufferBindingCollection(capacity);
        }

        protected virtual AttributeCollection CreateAttributeCollection(int capacity)
        {
            return new AttributeCollection(capacity);
        }

        protected virtual BufferCollection CreateBufferCollection(int capacity)
        {
            return new BufferCollection(capacity);
        }

        protected virtual VertexBufferBinding CreateVertexBufferBinding()
        {
            return new VertexBufferBinding();
        }

        protected virtual MaterialTexture CreateMaterialTexture()
        {
            return new MaterialTexture();
        }

        protected virtual MaterialTextureStack CreateMaterialTextureStack()
        {
            return new MaterialTextureStack();
        }

        public Model ReadModel()
        {
            var model = Model;
            ReadModel(ref model);
            return model;
        }

        protected virtual void ReadModel(ref Model model)
        {
            // Starts the whole ModelData by the magiccode "TKMD"
            // If the serializer don't find the TKMD, It will throw an
            // exception that will be catched by Load method.
            BeginChunk(ModelData.MagicCode);

            // Allocated the shared memory used to load this Model
            AllocateSharedMemory(Reader.ReadInt32());

            // Material section
            BeginChunk("MATL");
            model.Materials = ReadMaterials();
            EndChunk();

            // Bones section
            BeginChunk("BONE");
            model.Bones = ReadBones();
            EndChunk();

            // Skinned Bones section
            BeginChunk("SKIN");
            model.SkinnedBones = ReadBones();
            EndChunk();

            // Mesh section
            BeginChunk("MESH");
            model.Meshes = ReadMeshes();
            EndChunk();

            // Serialize attributes
            Serialize(ref model.Attributes);

            // Close TKMD section
            EndChunk();
        }

        protected virtual ModelBoneCollection ReadBones()
        {
            // Read all bones
            var bones = ReadList(CreateModelBoneCollection, CreateModelBone, ReadBone);

            // Fix all children bones
            int count = bones.Count;
            for (int i = 0; i < count; i++)
            {
                var bone = bones[i];
                // If bone has no children, then move on
                if (bone.Children == null) continue;

                var children = bone.Children;
                var childIndices = children.ChildIndices;
                foreach (int childIndex in childIndices)
                {
                    if (childIndex < 0)
                    {
                        children.Add(null);
                    }
                    else if (childIndex < count)
                    {
                        children.Add(bones[childIndex]);
                    }
                    else
                    {
                        throw new InvalidOperationException("Invalid children index for bone");
                    }
                }
                children.ChildIndices = null;
            }

            return bones;
        }

        protected virtual MaterialCollection ReadMaterials()
        {
            return ReadList(CreateModelMaterialCollection, CreateModelMaterial, ReadMaterial);
        }

        protected virtual ModelMeshCollection ReadMeshes()
        {
            return ReadList(CreateModelMeshCollection, CreateModelMesh, ReadMesh);
        }

        protected virtual VertexBufferBindingCollection ReadVertexBuffers()
        {
            return ReadList(CreateVertexBufferBindingCollection, CreateVertexBufferBinding, ReadVertexBuffer);
        }

        protected virtual ModelMeshPartCollection ReadMeshParts()
        {
            return ReadList(CreateModelMeshPartCollection, CreateModelMeshPart, ReadMeshPart);
        }

        protected virtual BufferCollection ReadIndexBuffers()
        {
            int count = Reader.ReadInt32();
            var list = CreateBufferCollection(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(ReadIndexBuffer());
            }
            return list;
        }

        protected virtual void ReadMaterial(ref Material material)
        {
            var textureCount = Reader.ReadInt32();
            for (int i = 0; i < textureCount; i++)
            {
                var materialTexture = CreateMaterialTexture();
                ReadMaterialTexture(ref materialTexture);

                switch (materialTexture.Type)
                {
                    case MaterialTextureType.Ambient:
                        AddMaterialTexture(ref material.Ambient, materialTexture);
                        break;
                    case MaterialTextureType.Diffuse:
                        AddMaterialTexture(ref material.Diffuse, materialTexture);
                        break;
                    case MaterialTextureType.Displacement:
                        AddMaterialTexture(ref material.Displacement, materialTexture);
                        break;
                    case MaterialTextureType.Emissive:
                        AddMaterialTexture(ref material.Emissive, materialTexture);
                        break;
                    case MaterialTextureType.Height:
                        AddMaterialTexture(ref material.Height, materialTexture);
                        break;
                    case MaterialTextureType.Lightmap:
                        AddMaterialTexture(ref material.Lightmap, materialTexture);
                        break;
                    case MaterialTextureType.Normals:
                        AddMaterialTexture(ref material.Normals, materialTexture);
                        break;
                    case MaterialTextureType.Opacity:
                        AddMaterialTexture(ref material.Opacity, materialTexture);
                        break;
                    case MaterialTextureType.Reflection:
                        AddMaterialTexture(ref material.Reflection, materialTexture);
                        break;
                    case MaterialTextureType.Shininess:
                        AddMaterialTexture(ref material.Shininess, materialTexture);
                        break;
                    case MaterialTextureType.Specular:
                        AddMaterialTexture(ref material.Specular, materialTexture);
                        break;
                    case MaterialTextureType.Unknown:
                        AddMaterialTexture(ref material.Unknown, materialTexture);
                        break;
                    case MaterialTextureType.None:
                        // Throws an exception for None?
                        //AddMaterialTexture(ref material.None, materialTexture);
                        break;
                    default:
                        throw new InvalidOperationException(string.Format("Invalid material texture type [{0}]", materialTexture.Type));
                        break;
                }
            }

            // TODO Improve serialization
            Serialize(ref material.Properties);
        }

        protected virtual void AddMaterialTexture(ref MaterialTextureStack textureStack, MaterialTexture texture)
        {
            if (textureStack == null)
            {
                textureStack = CreateMaterialTextureStack();
            }
            textureStack.Add(texture);
        }

        protected virtual void ReadMaterialTexture(ref MaterialTexture materialTexture)
        {
            // Loads the texture
            string filePath = null;
            Serialize(ref filePath);
            materialTexture.name = Path.GetFileNameWithoutExtension(filePath);
            materialTexture.Texture = TextureLoaderDelegate(filePath);

            materialTexture.Type = (MaterialTextureType)Reader.ReadByte();
            materialTexture.Index = Reader.ReadInt32();
            materialTexture.UVIndex = Reader.ReadInt32();
            materialTexture.BlendFactor = Reader.ReadSingle();
            materialTexture.Operation = (MaterialTextureOperator)Reader.ReadByte();
            materialTexture.WrapMode = (TextureAddressMode)Reader.ReadInt32();
            materialTexture.Flags = (MaterialTextureFlags)Reader.ReadByte();
        }

        protected virtual void ReadBone(ref ModelBone bone)
        {
            // Read Parent Index
            int parentIndex = Reader.ReadInt32();
            if (parentIndex > Model.Bones.Count)
            {
                throw new InvalidOperationException("Invalid index for parent bone");
            }
            bone.Parent = parentIndex >= 0 ? Model.Bones[parentIndex] : null;

            // Transform
            Serialize(ref bone.Transform);

            // Name
            Serialize(ref bone.name, false, SerializeFlags.Nullable);

            // Indices
            List<int> indices = null;
            Serialize(ref indices, Serialize, SerializeFlags.Nullable);
            if (indices != null)
            {
                bone.Children = CreateModelBoneCollection(indices.Count);
                bone.Children.ChildIndices = indices;
            }
        }

        protected virtual void ReadMesh(ref ModelMesh mesh)
        {
            Serialize(ref mesh.name, false, SerializeFlags.Nullable);
            int parentBoneIndex = Reader.ReadInt32();
            if (parentBoneIndex >= 0) mesh.ParentBone = Model.Bones[parentBoneIndex];

            mesh.VertexBuffers = ReadVertexBuffers();
            mesh.IndexBuffers = ReadIndexBuffers();
            mesh.MeshParts = ReadMeshParts();

            Serialize(ref mesh.Attributes);
        }

        protected virtual void ReadMeshPart(ref ModelMeshPart meshPart)
        {
            // Material
            int materialIndex = Reader.ReadInt32();
            meshPart.Material = Model.Materials[materialIndex];

            // IndexBuffer
            var indexBufferRange = default(ModelData.BufferRange);
            indexBufferRange.Serialize(this);
            meshPart.IndexBuffer = GetFromList(indexBufferRange, CurrentMesh.IndexBuffers);

            // VertexBuffer
            var vertexBufferRange = default(ModelData.BufferRange);
            vertexBufferRange.Serialize(this);
            meshPart.VertexBuffer = GetFromList(vertexBufferRange, CurrentMesh.VertexBuffers);

            // Attributes
            Serialize(ref meshPart.Attributes);
        }

        protected virtual void ReadVertexBuffer(ref VertexBufferBinding vertexBufferBinding)
        {
            // Read the number of vertices
            int count = Reader.ReadInt32();

            // Read vertex elements
            int vertexElementCount = Reader.ReadInt32();
            var elements = new VertexElement[vertexElementCount];
            for (int i = 0; i < vertexElementCount; i++)
            {
                elements[i].Serialize(this);
            }
            vertexBufferBinding.Layout = VertexInputLayout.New(0, elements);

            // Read Vertex Buffer
            int sizeInBytes = Reader.ReadInt32();
            SerializeMemoryRegion(SharedMemoryPointer, sizeInBytes);
            vertexBufferBinding.Buffer =  Buffer.New(GraphicsDevice, new DataPointer(SharedMemoryPointer, sizeInBytes), sizeInBytes / count, BufferFlags.VertexBuffer, ResourceUsage.Immutable);
        }

        protected virtual Buffer ReadIndexBuffer()
        {
            int indexCount = Reader.ReadInt32();
            int sizeInBytes = Reader.ReadInt32();
            SerializeMemoryRegion(SharedMemoryPointer, sizeInBytes);
            return Buffer.New(GraphicsDevice, new DataPointer(SharedMemoryPointer, sizeInBytes), sizeInBytes / indexCount, BufferFlags.IndexBuffer, ResourceUsage.Immutable);
        }

        protected delegate TLIST CreateListDelegate<out TLIST, TITEM>(int list) where TLIST : List<TITEM>;

        protected delegate T CreateItemDelegate<out T>();

        protected delegate void ReadItemDelegate<T>(ref T item);

        protected virtual TLIST ReadList<TLIST, TITEM>(CreateListDelegate<TLIST, TITEM> listCreate, CreateItemDelegate<TITEM> itemCreate, ReadItemDelegate<TITEM> itemReader) where TLIST : List<TITEM>
        {
            int count = Reader.ReadInt32();
            var list = listCreate(count);
            for (int i = 0; i < count; i++)
            {
                var item = itemCreate();
                itemReader(ref item);
                list.Add(item);
            }
            return list;
        }

        private ModelBufferRange<T> GetFromList<T>(ModelData.BufferRange range, IList<T> list)
        {
            var index = range.Slot;
            if (index >= list.Count)
            {
                throw new InvalidOperationException(string.Format("Invalid slot [{0}] for {1} (Max: {2})", index, typeof(T).Name, list.Count));
            }
            return new ModelBufferRange<T> { Resource = list[index], Count = range.Count, Start = range.Start };
        }
    }
}