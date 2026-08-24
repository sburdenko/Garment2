using Garment.Body;
using UnityEngine;

namespace Garment.Fitting
{
    /// <summary>
    /// Bind-pose snapshot of a body, shared by every garment bound to it.
    /// Rebuilt only when the body itself changes, not per garment.
    /// </summary>
    public sealed class BodySkinIndex
    {
        public Vector3[] Vertices { get; }
        public Vector3[] Normals { get; }
        public BoneWeight[] Weights { get; }
        public VertexGrid Grid { get; }
        public Transform[] Bones { get; }
        public Matrix4x4[] Bindposes { get; }
        public Transform RootBone { get; }

        private BodySkinIndex(Vector3[] vertices, Vector3[] normals, BoneWeight[] weights, Transform[] bones, Matrix4x4[] bindposes, Transform rootBone)
        {
            Vertices = vertices;
            Normals = normals;
            Weights = weights;
            Bones = bones;
            Bindposes = bindposes;
            RootBone = rootBone;
            Grid = new VertexGrid(vertices);
        }

        public static BodySkinIndex From(BodyRig body)
        {
            var renderer = body != null ? body.BodyMesh : null;
            if (renderer == null || renderer.sharedMesh == null)
            {
                Debug.LogError("BodySkinIndex: body has no SkinnedMeshRenderer with a mesh.");
                return null;
            }

            var mesh = renderer.sharedMesh;
            if (mesh.boneWeights == null || mesh.boneWeights.Length != mesh.vertexCount)
            {
                Debug.LogError($"BodySkinIndex: body mesh '{mesh.name}' has no per-vertex bone weights.");
                return null;
            }

            var normals = mesh.normals;
            if (normals == null || normals.Length != mesh.vertexCount)
            {
                Debug.LogError($"BodySkinIndex: body mesh '{mesh.name}' has no normals.");
                return null;
            }

            return new BodySkinIndex(mesh.vertices, normals, mesh.boneWeights, renderer.bones, mesh.bindposes, renderer.rootBone);
        }
    }
}
