using System.Collections.Generic;
using Garment.Body;
using UnityEngine;

namespace Garment.Fitting
{
    /// <summary>
    /// Turns a static garment mesh into a SkinnedMeshRenderer driven by the body's own bones.
    /// This is what makes an arbitrary CLO3D export follow a moving body without per-garment rigging.
    /// </summary>
    public static class GarmentBinder
    {
        public static GameObject Bind(BodyRig body, BodySkinIndex bodyIndex, GarmentDefinition definition)
        {
            if (body == null || bodyIndex == null || definition == null) return null;
            if (definition.SourcePrefab == null)
            {
                Debug.LogError($"Garment '{definition.DisplayName}': no source prefab assigned.");
                return null;
            }

            var preSkinned = definition.SourcePrefab.GetComponentInChildren<SkinnedMeshRenderer>();
            if (preSkinned != null && preSkinned.sharedMesh != null && preSkinned.sharedMesh.boneWeights.Length > 0)
                return BindPreSkinned(body, definition, preSkinned);

            if (!TryGetSourceMesh(definition, out var sourceMesh, out var materials)) return null;

            float armThreshold = ArmThreshold(body, definition);
            float armLift = ArmLift(body, sourceMesh, armThreshold);
            var mesh = BuildFittedMesh(sourceMesh, GarmentFitter.Compute(body, definition, sourceMesh, bodyIndex.Vertices),
                definition, body.GirthScale, body.ArmStretch, armThreshold, armLift);

            if (definition.ResolvePenetration && definition.FitMode == GarmentFitMode.AutoFit)
            {
                mesh.vertices = PenetrationResolver.Resolve(
                    mesh.vertices, mesh.triangles, bodyIndex.Vertices, bodyIndex.Normals, bodyIndex.Grid,
                    definition.SkinOffset, definition.PenetrationPasses, definition.PenetrationSmoothing);
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
            }

            mesh.boneWeights = SkinWeightTransfer.Transfer(
                mesh.vertices, mesh, definition, bodyIndex.Vertices, bodyIndex.Weights, bodyIndex.Grid,
                definition.WeightSmoothing, armThreshold);
            mesh.bindposes = bodyIndex.Bindposes;
            mesh.RecalculateBounds();

            var holder = new GameObject($"Garment_{definition.DisplayName}");
            holder.transform.SetParent(body.transform, false);

            var renderer = holder.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = bodyIndex.Bones;
            renderer.rootBone = bodyIndex.RootBone;
            renderer.sharedMaterials = ResolveMaterials(definition, materials, mesh.subMeshCount);
            renderer.updateWhenOffscreen = true;

            return holder;
        }

        /// <summary>Sideways distance beyond which an upper-body garment's vertices are sleeve.</summary>
        /// <summary>
        /// A garment skinned in a DCC against this project's exported skeleton: weights come
        /// from bone-heat, so nothing needs transferring — only the bones remapped onto the
        /// live rig. Blender's FBX round-trip mirrors X, flipping the mesh and its bones
        /// together, so the name map swaps Left and Right to land geometry back where it is.
        /// </summary>
        private static GameObject BindPreSkinned(BodyRig body, GarmentDefinition definition, SkinnedMeshRenderer source)
        {
            var bodySmr = body.BodyMesh;
            if (bodySmr == null || bodySmr.sharedMesh == null)
            {
                Debug.LogError($"{definition.DisplayName}: pre-skinned bind needs the body mesh for bind poses.");
                return null;
            }

            var bodyBoneIndex = new Dictionary<string, int>();
            for (int i = 0; i < bodySmr.bones.Length; i++)
                if (bodySmr.bones[i] != null) bodyBoneIndex[bodySmr.bones[i].name] = i;

            var sourceBones = source.bones;
            var bones = new Transform[sourceBones.Length];
            var bindposes = new Matrix4x4[sourceBones.Length];
            var bodyBindposes = bodySmr.sharedMesh.bindposes;
            int missing = 0;
            for (int i = 0; i < sourceBones.Length; i++)
            {
                string wanted = MirrorSideName(sourceBones[i] != null ? sourceBones[i].name : string.Empty);
                if (!bodyBoneIndex.TryGetValue(wanted, out int bodyIdx))
                {
                    bodyBoneIndex.TryGetValue("Hips", out bodyIdx);
                    missing++;
                }
                bones[i] = bodySmr.bones[bodyIdx];
                bindposes[i] = bodyBindposes[bodyIdx];
            }
            if (missing > 0) Debug.LogWarning($"{definition.DisplayName}: {missing} bone(s) fell back to Hips.");

            var mesh = Object.Instantiate(source.sharedMesh);
            mesh.name = definition.DisplayName + " (preskinned)";
            ApplyBodyShape(mesh, body);
            mesh.bindposes = bindposes;

            var holder = new GameObject($"Garment_{definition.DisplayName}");
            holder.transform.SetParent(body.transform, false);
            holder.transform.localPosition = Vector3.zero;
            holder.transform.localRotation = Quaternion.identity;

            var renderer = holder.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = bones;
            renderer.rootBone = bodySmr.rootBone;
            renderer.updateWhenOffscreen = true;
            renderer.sharedMaterials = ResolvePreSkinnedMaterials(source, mesh.subMeshCount);
            return holder;
        }

        /// <summary>Widen and lengthen a pre-skinned garment the same way fitted ones are.</summary>
        private static void ApplyBodyShape(Mesh mesh, BodyRig body)
        {
            if (body.GirthScale <= 1.01f && Mathf.Abs(body.ArmStretch - 1f) <= 0.01f) return;

            var vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                if (body.GirthScale > 1f)
                {
                    vertices[i].x *= body.GirthScale;
                    vertices[i].z *= body.GirthScale;
                }
            }
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
        }

        private static string MirrorSideName(string name)
        {
            if (name.StartsWith("Left")) return "Right" + name.Substring(4);
            if (name.StartsWith("Right")) return "Left" + name.Substring(5);
            return name;
        }

        private static Material[] ResolvePreSkinnedMaterials(SkinnedMeshRenderer source, int subMeshCount)
        {
            var fallback = source.sharedMaterials.Length > 0 ? source.sharedMaterials : null;
            var materials = new Material[subMeshCount];
            for (int i = 0; i < subMeshCount; i++)
                materials[i] = fallback != null ? fallback[Mathf.Min(i, fallback.Length - 1)] : null;
            return materials;
        }

        /// <summary>
        /// Rotates each sleeve so its axis matches the avatar's arm bone. Garments are authored
        /// in whatever pose their designer used — an A-pose sleeve bound onto a T-pose skeleton
        /// keeps its droop for good, because skinning only adds the arm's rotation on top of
        /// whatever direction the sleeve already had.
        /// </summary>
        public static void RectifySleeves(Mesh mesh, BodyRig body, float armThreshold)
        {
            if (armThreshold <= 0f) return;

            var leftShoulder = body.GetBone(BodyLandmark.LeftShoulder);
            var leftElbow = body.GetBone(BodyLandmark.LeftElbow);
            if (leftShoulder == null || leftElbow == null) return;

            var root = body.transform;
            var shoulderLocal = root.InverseTransformPoint(leftShoulder.position);
            var boneDirection = (root.InverseTransformPoint(leftElbow.position) - shoulderLocal);

            var vertices = mesh.vertices;
            bool changed = false;
            // The axis estimate shifts as the sleeve turns (drape skews the centroids), so
            // iterate: each pass measures again and turns by the remainder.
            for (int pass = 0; pass < 3; pass++)
            {
                bool passChanged = false;
                passChanged |= RectifySide(vertices, -1f, armThreshold, shoulderLocal.y, boneDirection);
                passChanged |= RectifySide(vertices, +1f, armThreshold, shoulderLocal.y, boneDirection);
                changed |= passChanged;
                if (!passChanged) break;
            }
            if (!changed) return;

            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private static bool RectifySide(Vector3[] vertices, float sign, float threshold, float shoulderY, Vector3 boneDirection)
        {
            const float SeamBlendStart = -0.04f, SeamBlendEnd = 0.06f;
            // Only geometry near shoulder height is sleeve. The hem of a wide garment spans the
            // same X range, and without this cut it pollutes the sleeve axis so badly that the
            // computed rotation is meaningless — the sleeve never actually straightens.
            float sleeveYMin = shoulderY - 0.28f;

            float reach = 0f;
            for (int i = 0; i < vertices.Length; i++)
            {
                if (vertices[i].y < sleeveYMin) continue;
                float along = vertices[i].x * sign - threshold;
                if (along > reach) reach = along;
            }
            if (reach < 0.08f) return false;

            Vector3 near = Vector3.zero, far = Vector3.zero;
            int nearCount = 0, farCount = 0;
            float nearTop = float.MinValue, farTop = float.MinValue;
            for (int i = 0; i < vertices.Length; i++)
            {
                if (vertices[i].y < sleeveYMin) continue;
                float along = vertices[i].x * sign - threshold;
                if (along < 0f) continue;
                if (along < reach * 0.25f)
                {
                    near += vertices[i]; nearCount++;
                    if (vertices[i].y > nearTop) nearTop = vertices[i].y;
                }
                else if (along > reach * 0.7f)
                {
                    far += vertices[i]; farCount++;
                    if (vertices[i].y > farTop) farTop = vertices[i].y;
                }
            }
            if (nearCount == 0 || farCount == 0) return false;

            // The axis follows the sleeve's UPPER edge, not the centroid: drape hangs off the
            // underside and drags a centroid axis down, so centroids overshoot — while the eye
            // judges a sleeve by whether its top edge runs along the arm.
            var nearC = near / nearCount;
            var farC = far / farCount;
            var sleeveAxis = new Vector3(farC.x - nearC.x, farTop - nearTop, farC.z - nearC.z);
            var wanted = new Vector3(Mathf.Abs(boneDirection.x) * sign, boneDirection.y, boneDirection.z);
            if (sleeveAxis.sqrMagnitude < 1e-6f || wanted.sqrMagnitude < 1e-6f) return false;

            // One rigid turn about the shoulder seam: keeps the sleeve tube intact where
            // per-slice straightening shreds a strongly angled sleeve.
            var rotation = Quaternion.FromToRotation(sleeveAxis.normalized, wanted.normalized);
            if (Quaternion.Angle(Quaternion.identity, rotation) < 2f) return false;

            var pivot = new Vector3(sign * threshold, shoulderY, (near / nearCount).z);
            for (int i = 0; i < vertices.Length; i++)
            {
                if (vertices[i].y < sleeveYMin) continue;
                float along = vertices[i].x * sign - threshold;
                if (along < SeamBlendStart) continue;

                float weight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(SeamBlendStart, SeamBlendEnd, along));
                if (weight <= 0f) continue;

                var rotated = pivot + rotation * (vertices[i] - pivot);
                vertices[i] = Vector3.Lerp(vertices[i], rotated, weight);
            }
            return true;
        }

        public static float ArmThreshold(BodyRig body, GarmentDefinition definition)
        {
            if (definition.Slot != GarmentSlot.Top && definition.Slot != GarmentSlot.Outer) return 0f;

            var leftShoulder = body.GetBone(BodyLandmark.LeftShoulder);
            var rightShoulder = body.GetBone(BodyLandmark.RightShoulder);
            if (leftShoulder == null || rightShoulder == null) return 0f;

            return Vector3.Distance(leftShoulder.position, rightShoulder.position) * 0.5f + 0.03f;
        }

        private static Material[] ResolveMaterials(GarmentDefinition definition, Material[] imported, int submeshCount)
        {
            var resolved = new Material[submeshCount];
            for (int i = 0; i < submeshCount; i++)
            {
                var fallback = imported != null && i < imported.Length ? imported[i] : null;
                resolved[i] = definition.MaterialFor(i, fallback);
            }
            return resolved;
        }

        private static bool TryGetSourceMesh(GarmentDefinition definition, out Mesh mesh, out Material[] materials)
        {
            mesh = null;
            materials = null;

            var filter = definition.SourcePrefab.GetComponentInChildren<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                mesh = filter.sharedMesh;
                var renderer = filter.GetComponent<MeshRenderer>();
                materials = renderer != null ? renderer.sharedMaterials : new Material[mesh.subMeshCount];
                return true;
            }

            var skinned = definition.SourcePrefab.GetComponentInChildren<SkinnedMeshRenderer>();
            if (skinned != null && skinned.sharedMesh != null)
            {
                mesh = skinned.sharedMesh;
                materials = skinned.sharedMaterials;
                return true;
            }

            Debug.LogError($"Garment '{definition.DisplayName}': prefab has no mesh.");
            return false;
        }

        /// <summary>
        /// How far to raise the sleeve so its upper edge sits on top of the measured arm.
        /// The tracked bone runs through the arm's centre; a sleeve cut for a slimmer avatar
        /// has its top edge too close to that centre and the wearer's arm shows above it.
        /// </summary>
        private static float ArmLift(BodyRig body, Mesh sourceMesh, float armThresholdX)
        {
            if (armThresholdX <= 0f || body.ArmRadius <= 0f) return 0f;

            var shoulderBone = body.GetBone(BodyLandmark.LeftShoulder);
            if (shoulderBone == null) return 0f;
            float boneY = body.transform.InverseTransformPoint(shoulderBone.position).y;

            float sleeveTop = float.MinValue;
            foreach (var vertex in sourceMesh.vertices)
                if (Mathf.Abs(vertex.x) > armThresholdX) sleeveTop = Mathf.Max(sleeveTop, vertex.y);
            if (sleeveTop <= float.MinValue) return 0f;

            float clearance = sleeveTop - boneY;
            return Mathf.Clamp(body.ArmRadius + 0.01f - clearance, 0f, 0.08f);
        }

        private static Mesh BuildFittedMesh(Mesh source, GarmentFit fit, GarmentDefinition definition,
            float girthScale, float armStretch, float armThresholdX, float armLift)
        {
            var mesh = Object.Instantiate(source);
            mesh.name = $"{definition.DisplayName}_Fitted";

            var vertices = source.vertices;
            var normals = source.normals;
            bool hasNormals = normals != null && normals.Length == vertices.Length;

            var inverseScale = new Vector3(
                SafeInverse(fit.Scale.x),
                SafeInverse(fit.Scale.y),
                SafeInverse(fit.Scale.z));

            var fitted = new Vector3[vertices.Length];
            var fittedNormals = hasNormals ? new Vector3[vertices.Length] : null;

            for (int i = 0; i < vertices.Length; i++)
            {
                var position = fit.Apply(vertices[i]);
                if (hasNormals)
                {
                    var normal = Vector3.Scale(normals[i], inverseScale).normalized;
                    fittedNormals[i] = normal;
                    position += normal * definition.SkinOffset;
                }
                if (girthScale > 1f)
                {
                    // Widen around the body's vertical axis — the wearer is broader than the
                    // avatar this garment was authored on.
                    position.x *= girthScale;
                    position.z *= girthScale;
                }
                if (armThresholdX > 0f && Mathf.Abs(position.x) > armThresholdX - 0.02f)
                {
                    float armWeight = Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(armThresholdX - 0.02f, armThresholdX + 0.06f, Mathf.Abs(position.x)));

                    if (Mathf.Abs(armStretch - 1f) > 0.01f && Mathf.Abs(position.x) > armThresholdX)
                    {
                        // Lengthen the sleeve along the arm — the wearer's arms are longer than
                        // the avatar's, and bone lengths must stay untouched.
                        float sign = Mathf.Sign(position.x);
                        position.x = sign * (armThresholdX + (Mathf.Abs(position.x) - armThresholdX) * armStretch);
                    }

                    // Raise the sleeve onto the measured arm, fading in across the shoulder.
                    position.y += armLift * armWeight;
                }
                fitted[i] = position;
            }

            mesh.vertices = fitted;
            if (hasNormals) mesh.normals = fittedNormals;
            mesh.RecalculateTangents();
            return mesh;
        }

        private static float SafeInverse(float value) => Mathf.Abs(value) < 1e-5f ? 1f : 1f / value;
    }
}
