using Godot;
using System.Collections.Generic;

// RiderParts: per-rider breakable-part registry for the duel. Owns the part
// model (which meshes belong to which part, per-part HP), per-mesh hit
// volumes anchored to skeleton bones, and hit attribution.
//
// Part mapping (from the imported node names + per-vertex skin weights -
// every mesh in both FBXs is rigidly bound 100% to a single bone):
//   TW    torso = Torso core + TorsoPin + Waist (the main HP pool)
//         pod_L/pod_R = the LRM ear boxes, SPLIT out of the single Torso
//           mesh at runtime (the kit's torso STL merges ears+torso into
//           one mesh, so the ears are separated here by triangle region)
//         head  = canopy region split out of Torso + Antenna
//         arm_L/R = UpperArm + ShoulderPad + Elbow + Forearm + HandGun
//           (+ WeaponERPPC), all shoulder/elbow-bound
//         leg_L/R = Thigh + Shin + FootHub + all Toe meshes
//   HECTOR torso = Torso core + Pelvis + AC20 + SRM6 (per spec the guns
//           ride with the torso part)
//         head  = skull cap region split out of Torso
//         arm_L/R = Shoulder + UpperArm + Elbow + Forearm
//         leg_L/R = Thigh + Shin + Foot
//
// Volumes: each mesh contributes one box in its bone's frame, derived from
// the mesh AABB at bind pose. A world point is transformed into each
// bone's frame and the box signed-distance decides attribution, so the
// volumes follow articulation (leg bends, torso twist) exactly. Attribution
// always returns the nearest part (torso included), destroyed parts are
// skipped once they break off.
public sealed class RiderParts
{
    public sealed class Volume
    {
        public int Bone;
        public Vector3 CenterB;
        public Vector3 HalfB;
    }

    public sealed class MeshRec
    {
        public MeshInstance3D Mi;
        public Node OrigParent;
        public Transform3D OrigLocal;
        public Skin OrigSkin;
        public NodePath OrigSkeleton;
        public int Bone;
        public int BindIndex;
        public string BoneName;
        public bool Detached;
        // Saved overlay for the wreck state (restored on unwreck/reset;
        // the wreck transform itself is rebuilt from OrigLocal).
        public Material SavedOverlay;
    }

    public sealed class Debris
    {
        public Part Part;
        public Node3D Node;
        public Vector3 Vel;
        public Vector3 AngAxisW = Vector3.Up;
        public float AngSpeed;
        public bool Settled;
        public float RestMinY;
        public float RestGroundY;
    }

    public sealed class Part
    {
        public string Id;
        public string Display;
        public float MaxHp;
        public float Hp;
        public bool Destroyed;
        // Legs only: destroyed but still attached (darkened + deformed),
        // per the owner's rule that legs never fall off.
        public bool Wrecked;
        public readonly List<MeshRec> Meshes = new();
        public readonly List<Volume> Volumes = new();
    }

    private readonly RiderWalker _walker;
    private readonly Skeleton3D _skel;
    private readonly string _riderId;
    private readonly Dictionary<string, Part> _parts = new();

    public string RiderId => _riderId;
    public System.Action<string, Vector3> OnBreak; // (partId, worldCenter)

    public RiderParts(RiderWalker walker, string riderId)
    {
        _walker = walker;
        _riderId = riderId;
        _skel = walker.Skeleton;

        var byName = new Dictionary<string, MeshInstance3D>();
        CollectMeshes(walker.ModelRoot, byName);

        // Rider paint lives in the textures, never in vertex colours.
        // An export that carries a stray colour layer (the owner's
        // 2026-11-21 Hector Torso: near-black values, the only mesh in
        // the file with a colour slot) turns the importer's
        // vertex-colour-as-albedo flag into a black part in-game while
        // Blender shows the texture fine. Neutralise the flag on every
        // rider material before the torso carve copies materials.
        StripVertexColorAlbedo(byName);

        if (riderId == "enyo")
        {
            // Z thresholds carry a +0.09 frame shift (2026-11-15):
            // SplitTorso now classifies in the model's own frame;
            // these regions were measured in the old walker-relative
            // frame, whose origin sat 0.09 lower in Z for this model.
            SplitTorso(byName, new (string, System.Func<Vector3, bool>)[]
            {
                ("Pod_L", p => p.X >= 1.7f && p.Y >= 9.9f && p.Z <= -1.01f),
                ("Pod_R", p => p.X <= -1.7f && p.Y >= 9.9f && p.Z <= -1.01f),
                ("Head", p => Mathf.Abs(p.X) <= 1.4f && p.Y >= 10.1f && p.Z >= 1.09f),
            });
            AddPart("torso", "TORSO", 0f, byName, "TorsoCore", "TorsoPin", "Waist");
            AddPart("pod_L", "LEFT LRM POD", 45f, byName, "TorsoPod_L");
            AddPart("pod_R", "RIGHT LRM POD", 45f, byName, "TorsoPod_R");
            AddPart("head", "HEAD", 50f, byName, "TorsoHead", "Antenna");
            AddPart("arm_L", "LEFT ARM", 90f, byName,
                "UpperArm_L", "ShoulderPad_L", "Elbow_L", "Forearm_L", "HandGun_L", "WeaponERPPC_L");
            AddPart("arm_R", "RIGHT ARM", 90f, byName,
                "UpperArm_R", "ShoulderPad_R", "Elbow_R", "Forearm_R", "HandGun_R", "WeaponERPPC_R");
            AddPart("leg_L", "LEFT LEG", 90f, byName,
                "Thigh_L", "Shin_L", "FootHub_L", "ToeFL1_L", "ToeFL2_L", "ToeFR1_L", "ToeFR2_L", "ToeBack1_L", "ToeBack2_L");
            AddPart("leg_R", "RIGHT LEG", 90f, byName,
                "Thigh_R", "Shin_R", "FootHub_R", "ToeFL1_R", "ToeFL2_R", "ToeFR1_R", "ToeFR2_R", "ToeBack1_R", "ToeBack2_R");
        }
        else if (riderId == "hector")
        {
            // Merged re-import (owner's part-merged FBX, 2026-11-21): 33 meshes instead of
            // 208, rig transferred from the previous build by vertex-position match (his
            // re-export is re-decimated, -3,584 tris, positions within 0.13u in file
            // units). The two cannons stay whole as the waist gun; the head is still
            // carved from Torso by SplitTorso below. (2026-11-21 evening: the owner's
            // own re-export from the reverted history - 16 objects with the clean part
            // names, his rig and UVs, textures as Hector.fbm sidecars like Apollo's.
            // Used AS IS. His merge folded the SRM pod into Left_Shoulder, so pod_L
            // claims no mesh until the pod is its own object again.)
            // Owner's clean split (2026-11-17): his 16 named
            // objects plus the Tripo fragments, rigged as-is -
            // nothing moved or regrouped. Head = the skull
            // band of his Torso object (re-measured on his
            // 2026-11-21 re-export: his head-group verts span
            // |X| <= 0.146, Y >= 0.848, Z >= -0.108 in file
            // units; the band below covers them - the shipped
            // band caught only 341 of his 614 head verts and
            // the brk rays attributed the skull to torso). Gun = his cannon group under
            // Torso (Left/Right_Cannon.001 housings + the
            // Cannon barrels), torso-bound per his grouping
            // (2026-11-18 restructure: Ares legs + pelvis
            // replaced the native bottom half; the old
            // tripo_part_33 island name is gone). Pod = the connected armour cluster
            // over the left shoulder, contact-chained from
            // tripo_part_53 - the same pieces the previous
            // build joined as SRM6.
            SplitTorso(byName, new (string, System.Func<Vector3, bool>)[]
            {
                ("Head", p => Mathf.Abs(p.X) <= 0.15f && p.Y >= 0.84f && p.Z >= -0.14f),
            });
            AddPart("torso", "TORSO", 0f, byName, "Pelvis", "TorsoCore");
            AddPart("gun", "WAIST GUN", 65f, byName, "Left_Cannon", "Right_Cannon");
            AddPart("pod_L", "SRM POD", 45f, byName);
            AddPart("head", "HEAD", 50f, byName, "TorsoHead");
            AddPart("arm_L", "LEFT ARM", 90f, byName, "Left_Elbow", "Left_Hand", "Left_Shoulder");
            AddPart("arm_R", "RIGHT ARM", 90f, byName, "Right_Elbow", "Right_Hand", "Right_Shoulder");
            AddPart("leg_L", "LEFT LEG", 90f, byName, "Left_Foot", "Left_Hip", "Left_Shin");
            AddPart("leg_R", "RIGHT LEG", 90f, byName, "Right_Foot", "Right_Hip", "Right_Shin");
            AttachDebris(byName);
        }
        else if (riderId == "hermes")
        {
            // Merged re-import (owner's part-merged FBX, 2026-11-21): 14 meshes instead of
            // 210, vertex-exact against the previous build. His export was only partly
            // merged (~115 tripo fragments still separate, some skinned to his own
            // armature, which was discarded); the transfer folded them into the named
            // parts by bone and part. Layout claims the merged names.
            // Owner's own grouping (Hermes-Clean.fbx, 2026-11-18,
            // replacing the Tripo island build): 12 named part
            // objects with the fragments parented under them; the
            // build binds every fragment rigidly to its root
            // object's bone on the file's own Mixamo skeleton
            // (renamed to the game bones), so the safety net below
            // files each unclaimed fragment into the part its bone
            // belongs to. Head and the LRM rack bind torso like
            // the rest of the chest, so they are claimed by name:
            // the rack is the honeycomb cells and housing over
            // the left shoulder, the head is the cockpit cap and
            // antennae on top of the torso.
            AddPart("torso", "TORSO", 0f, byName, "Pelvis", "Torso");
            AddPart("pod_L", "LRM RACK", 45f, byName, "tripo_part_34");
            AddPart("head", "HEAD", 50f, byName, "tripo_part_4");
            AddPart("arm_L", "LEFT ARM", 90f, byName, "Left_Cannon", "Left_Shoulder");
            AddPart("arm_R", "RIGHT ARM", 90f, byName, "Right_Cannon", "Right_Shoulder");
            AddPart("leg_L", "LEFT LEG", 90f, byName, "Left_Foot", "Left_Hip", "Left_Shin");
            AddPart("leg_R", "RIGHT LEG", 90f, byName, "Right_Foot", "Right_Hip", "Right_Shin");
            AttachDebris(byName);
        }
        else if (riderId == "apollo")
        {
            // Merged re-import (owner's part-merged FBX, 2026-11-21): 20 meshes instead of
            // 221. Binding and UVs transferred PER FACE from the previous build (each of
            // his faces takes the bone and UVs of the nearest previous-build triangle),
            // after his export arrived with its UV maps missing (the rider rendered
            // black) and a shell-level bind left joint-spanning geometry on one bone.
            // Layout claims the merged names; the head is still carved from Torso by
            // SplitTorso below.
            // Owner's own split (Apollo-Clean.fbx, 2026-11-17),
            // used AS IS at his instruction: no object moved,
            // renamed, joined or regrouped - the build only adds
            // the armature and a rigid bind per object, so the
            // meshes carry HIS object names and the parts claim
            // them by those names. The 216 ungrouped fragments
            // keep their own objects too; the safety net below
            // sweeps them into their nearest part at load. The
            // helmet is fused into his Torso object, so the head
            // is carved here (band = the build's head vertex
            // group: |x| <= 1.45, top of the torso). The
            // Shoulder_Cannon he split out is the missile pod.
            SplitTorso(byName, new (string, System.Func<Vector3, bool>)[]
            {
                ("Head", p => Mathf.Abs(p.X) <= 1.45f && p.Y >= 11.30f),
            });
            AddPart("torso", "TORSO", 0f, byName, "Pelvis", "Pelvis__torso", "TorsoCore");
            AddPart("pod_L", "SHOULDER CANNON", 45f, byName, "Shoulder_Cannon");
            AddPart("head", "HEAD", 50f, byName, "TorsoHead");
            AddPart("arm_L", "LEFT ARM", 90f, byName, "Left_Elbow", "Left_Elbow__shoulder_L", "Left_Hand", "Left_Shoulder");
            AddPart("arm_R", "RIGHT ARM", 90f, byName, "Right_Elbow", "Right_Shoulder");
            AddPart("leg_L", "LEFT LEG", 90f, byName, "Left_Foot", "Left_Hip", "Left_Hip__pelvis", "Left_Shin", "Left_Shin__hip_L");
            AddPart("leg_R", "RIGHT LEG", 90f, byName, "Right_Foot", "Right_Hip", "Right_Hip__pelvis", "Right_Shin", "Right_Shin__hip_R");
            AttachDebris(byName);
        }
        else if (riderId == "ares")
        {
            // Merged re-import (owner's part-merged FBX, 2026-11-21): 18 meshes instead of
            // 265, vertex-exact against the previous build. Layout claims the merged
            // names; the head is still carved from Torso by SplitTorso below.
            // Owner's own split (Ares.fbx, 2026-11-18), replacing
            // the Tripo island build: 12 named part objects with
            // every fragment parented under them; the build binds
            // each mesh rigidly to its root object's bone on the
            // file's own Mixamo skeleton, so the safety net files
            // unclaimed fragments by bone. No arm adjustment: the
            // sculpt's arms are modelled bent at 90 degrees, guns
            // forward (owner's rule for replaced models). Two
            // name claims: his "Torso" object IS the head assembly
            // (dome, face guard, chin) riding on a small sternum
            // tab - SplitTorso carves the head off the tab (clean
            // triangle gap at file z 0.834-0.855; SplitTorso
            // classifies in the file's own frame, and this file is
            // 1.0 tall, so the band is 0.845), and the face
            // plates plus the two back-pylon panels the previous
            // build counted as head are claimed by name. The pod
            // is the left shoulder drum group (the previous
            // build's pod was the left shoulder cap assembly):
            // the drum and its cradle children bind shoulder_L
            // but are claimed here by name.
            SplitTorso(byName, new (string, System.Func<Vector3, bool>)[]
            {
                ("Head", p => p.Y >= 0.845f),
            });
            AddPart("torso", "TORSO", 0f, byName, "Pelvis", "TorsoCore");
            AddPart("pod_L", "LEFT SHOULDER", 45f, byName, "Left_Shoulder");
            AddPart("head", "HEAD", 50f, byName, "TorsoHead", "tripo_part_132", "tripo_part_136", "tripo_part_143", "tripo_part_153", "tripo_part_154", "tripo_part_181");
            AddPart("arm_L", "LEFT ARM", 90f, byName, "Left_Cannon");
            AddPart("arm_R", "RIGHT ARM", 90f, byName, "Right_Cannon", "Right_Shoulder");
            AddPart("leg_L", "LEFT LEG", 90f, byName, "Left_Foot", "Left_Hip", "Left_Shin");
            AddPart("leg_R", "RIGHT LEG", 90f, byName, "Right_Foot", "Right_Hip", "Right_Shin");
            AttachDebris(byName);
        }
        else if (riderId == "hades")
        {
            // Merged re-import (owner's part-merged FBX, 2026-11-21): 19 meshes instead of
            // 215, rig transferred from the previous build by vertex-position match (max
            // deviation 0.08u). Layout claims the merged names; the left-shoulder pod
            // cluster stays separate, as before.
            // Owner's own split (Hades.fbx, 2026-11-19), replacing
            // the Tripo island build: 12 named part groups with
            // every fragment parented under them; the build binds
            // each mesh rigidly to its group root's bone (no
            // skeleton in his file - the rig is authored at his
            // groups' measured joints). No arm adjustment: his
            // arm positioning ships as modelled. Two name calls:
            // the head is the cockpit assembly grouped under
            // Torso (its own mesh is the fin cap, tripo_part_16
            // the canopy block, 145/148 the cheek guards, 179
            // the top cap - isolation-verified), bound to the
            // head bone; the sculpt has no missile pod, so the
            // left shoulder's top cap cluster (142 + the small
            // plates above the pauldron) serves as pod_L - it
            // binds shoulder_L and rides the arm.
            AddPart("torso", "TORSO", 0f, byName, "Pelvis", "Torso");
            AddPart("pod_L", "LEFT SHOULDER", 45f, byName, "tripo_part_142", "tripo_part_4_001");
            AddPart("head", "HEAD", 50f, byName, "Torso_head", "tripo_part_145", "tripo_part_148", "tripo_part_16", "tripo_part_179");
            AddPart("arm_L", "LEFT ARM", 90f, byName, "Left_Cannon", "Left_Shoulder");
            AddPart("arm_R", "RIGHT ARM", 90f, byName, "Right_Cannon", "Right_Shoulder");
            AddPart("leg_L", "LEFT LEG", 90f, byName, "Left_Foot", "Left_Hip", "Left_Shin");
            AddPart("leg_R", "RIGHT LEG", 90f, byName, "Right_Foot", "Right_Hip", "Right_Shin");
            AttachDebris(byName);
        }
        else if (riderId == "amadeus")
        {
            // Owner's merged re-split (Amadeus_0_dz4x, 2026-11-21):
            // the same geometry as the 2026-11-19 split (vertex-exact,
            // 39,444 tris) with each part group's fragments merged
            // into a single mesh per body part - 14 meshes instead of
            // 244. The rig is the shipped build's armature, weights
            // transferred by exact vertex position; the head cluster
            // (ex tripo_part_10/11/124/169) and the right-shoulder
            // launcher cluster (ex tripo_part_88 + the 33-38 tube row
            // + 118/159/202/202 fittings) stay separate meshes so
            // both parts still break and move as before. His right
            // leg groups now carry proper Right_* names.
            AddPart("torso", "TORSO", 0f, byName, "Pelvis", "Torso");
            AddPart("pod_R", "RIGHT SHOULDER", 45f, byName, "Pod_R");
            AddPart("head", "HEAD", 50f, byName, "Head");
            AddPart("arm_L", "LEFT ARM", 90f, byName, "Left_Cannon", "Left_Shoulder");
            AddPart("arm_R", "RIGHT ARM", 90f, byName, "Right_Cannon", "Right_Shoulder");
            AddPart("leg_L", "LEFT LEG", 90f, byName, "Left_Foot", "Left_Hip", "Left_Shin");
            AddPart("leg_R", "RIGHT LEG", 90f, byName, "Right_Foot", "Right_Hip", "Right_Shin");
            AttachDebris(byName);
        }
        else if (riderId == "prometheus")
        {
            // Hellbringer kit: the head is the cockpit hood region
            // split out of the Torso mesh (measured on the built
            // model: |x| <= 1.3, top front). The missile pod on
            // the LEFT shoulder (owner, 2026-11-12: 'obvious pods
            // for missiles on its left shoulder') is fused into
            // the same Torso mesh - the census puts it at
            // X 1.10..2.28, height 10.6..12.1 (610 verts, a
            // distinct block above the shoulder line; axis
            // mapping cross-checked against Enyo's pod census),
            // so SplitTorso carves it as a real pod part.
            SplitTorso(byName, new (string, System.Func<Vector3, bool>)[]
            {
                ("Pod_L", p => p.X >= 1.12f && p.Y >= 10.55f),
                // Classification space note: measured on the
                // built model - the cockpit is the top of the
                // torso's dense front slab, which lands at
                // Z >= -0.72 in this space (region census +
                // per-mesh AABB checks), not at the gait-front
                // sign one would guess from the other riders.
                ("Head", p => Mathf.Abs(p.X) <= 1.3f && p.Y >= 11.0f && p.Z >= -0.72f),
            });
            AddPart("torso", "TORSO", 0f, byName, "TorsoCore", "TorsoPin", "Waist");
            AddPart("pod_L", "LEFT POD", 45f, byName, "TorsoPod_L");
            AddPart("head", "HEAD", 50f, byName, "TorsoHead");
            AddPart("arm_L", "LEFT ARM", 90f, byName,
                "ShoulderAct_L", "UpperArm_L", "UpperArm2_L", "Elbow_L", "Forearm_L", "HandGun_L", "WeaponERPPC_L");
            AddPart("arm_R", "RIGHT ARM", 90f, byName,
                "ShoulderAct_R", "UpperArm_R", "UpperArm2_R", "Elbow_R", "Forearm_R", "HandGun_R", "WeaponERPPC_R");
            AddPart("leg_L", "LEFT LEG", 90f, byName,
                "Thigh_L", "Knee_L", "Shin_L", "Foot_L");
            AddPart("leg_R", "RIGHT LEG", 90f, byName,
                "Thigh_R", "Knee_R", "Shin_R", "Foot_R");
        }
        else if (riderId == "medea")
        {
            // Shield mech, owner's clean split (2026-11-18):
            // his named objects ARE the parts - the turret is
            // its own object (the head), the left-shoulder
            // block is the missile pod, the shield and its pin
            // ride the left arm, the twin-barrel Cannon is the
            // right arm, and the legs are Enyo's (hub + toes).
            AddPart("torso", "TORSO", 0f, byName, "Torso", "Pelvis");
            AddPart("head", "HEAD", 45f, byName, "Tower");
            AddPart("arm_L", "LEFT ARM", 85f, byName, "Shield", "Cylinder");
            AddPart("arm_R", "RIGHT ARM", 85f, byName, "Cannon");
            AddPart("leg_L", "LEFT LEG", 80f, byName, "Thigh_L", "Shin_L", "FootHub_L",
                "ToeFL1_L", "ToeFL2_L", "ToeFR1_L", "ToeFR2_L", "ToeBack1_L", "ToeBack2_L");
            AddPart("leg_R", "RIGHT LEG", 80f, byName, "Thigh_R", "Shin_R", "FootHub_R",
                "ToeFL1_R", "ToeFL2_R", "ToeFR1_R", "ToeFR2_R", "ToeBack1_R", "ToeBack2_R");
            AddPart("pod_L", "LEFT SHOULDER", 40f, byName, "Cabin");
        }
        else if (riderId == "demeter")
        {
            // Dire Wolf upper body (owner's hand split) on the Enyo
            // leg rig. Head = cockpit dome split, Pod_L = the left
            // shoulder rack split (bands measured on the mesh).
            SplitTorso(byName, new (string, System.Func<Vector3, bool>)[]
            {
                ("Head", p => Mathf.Abs(p.X) <= 1.1f && p.Z <= -2.4f && p.Y >= 9.2f && p.Y <= 11.6f),
                ("Pod_L", p => p.X >= 1.6f && p.Y >= 12.0f),
            });
            AddPart("torso", "TORSO", 0f, byName, "TorsoCore", "TorsoPin", "Waist");
            AddPart("head", "HEAD", 50f, byName, "TorsoHead");
            AddPart("pod_L", "LEFT SHOULDER", 45f, byName, "TorsoPod_L");
            AddPart("arm_L", "LEFT ARM", 95f, byName, "GunArm_L", "Elbow_L");
            AddPart("arm_R", "RIGHT ARM", 95f, byName, "GunArm_R", "Elbow_R");
            AddPart("leg_L", "LEFT LEG", 95f, byName,
                "Thigh_L", "Shin_L", "FootHub_L", "ToeFL1_L", "ToeFL2_L", "ToeFR1_L", "ToeFR2_L", "ToeBack1_L", "ToeBack2_L");
            AddPart("leg_R", "RIGHT LEG", 95f, byName,
                "Thigh_R", "Shin_R", "FootHub_R", "ToeFL1_R", "ToeFL2_R", "ToeFR1_R", "ToeFR2_R", "ToeBack1_R", "ToeBack2_R");
        }
        else if (riderId == "theseus")
        {
            // Hauptmann upper body (owner's hand split) on the Enyo
            // leg rig. Head = cockpit prow split; the sculpt's LRM
            // rack sits on the RIGHT shoulder (bands measured).
            SplitTorso(byName, new (string, System.Func<Vector3, bool>)[]
            {
                ("Head", p => Mathf.Abs(p.X) <= 1.0f && p.Z <= -0.5f && p.Y >= 11.2f && p.Y <= 12.7f),
                ("Pod_R", p => p.X <= -1.7f && p.Y >= 10.8f),
            });
            AddPart("torso", "TORSO", 0f, byName, "TorsoCore", "TorsoPin", "Waist");
            AddPart("head", "HEAD", 50f, byName, "TorsoHead");
            AddPart("pod_R", "RIGHT SHOULDER", 45f, byName, "TorsoPod_R");
            AddPart("arm_L", "LEFT ARM", 95f, byName, "ShoulderPad_L", "GunArm_L", "Elbow_L");
            AddPart("arm_R", "RIGHT ARM", 95f, byName, "ShoulderPad_R", "GunArm_R", "Elbow_R");
            AddPart("leg_L", "LEFT LEG", 95f, byName,
                "Thigh_L", "Shin_L", "FootHub_L", "ToeFL1_L", "ToeFL2_L", "ToeFR1_L", "ToeFR2_L", "ToeBack1_L", "ToeBack2_L");
            AddPart("leg_R", "RIGHT LEG", 95f, byName,
                "Thigh_R", "Shin_R", "FootHub_R", "ToeFL1_R", "ToeFL2_R", "ToeFR1_R", "ToeFR2_R", "ToeBack1_R", "ToeBack2_R");
        }


        else
        {
            // Unknown chassis: no hand-authored layout. Fail LOUD
            // and fall back to a single torso part (the whole
            // model) so the duel still runs - author a real
            // layout here before shipping the rider.
            GD.PrintErr($"PARTS: no layout for '{riderId}' - torso-only fallback; author a layout in RiderParts.cs");
            var all = new string[byName.Count];
            byName.Keys.CopyTo(all, 0);
            AddPart("torso", "TORSO", 0f, byName, all);
        }
        // Safety net: EVERY mesh must belong to a part. A mesh
        // no layout claimed never detaches - it stays skinned to
        // the dead rider's skeleton and floats at the death
        // position while every real part lies on the ground
        // (owner report, 2026-11-17). Attach any leftover to the
        // part whose claimed meshes sit nearest to it.
        {
            var claimed = new System.Collections.Generic.HashSet<MeshInstance3D>();
            var partCenters = new System.Collections.Generic.Dictionary<string, Vector3>();
            foreach (var part in _parts.Values)
            {
                var acc = Vector3.Zero; int n = 0;
                foreach (var rec in part.Meshes)
                {
                    claimed.Add(rec.Mi);
                    acc += rec.Mi.GetAabb().GetCenter(); n++;
                }
                if (n > 0) partCenters[part.Id] = acc / n;
            }
            foreach (var kv in byName)
            {
                if (claimed.Contains(kv.Value)) continue;
                // Prefer the part the mesh's own bone belongs to
                // (a fragment bound to an elbow should fall with
                // the arm, wherever its centre sits); nearest
                // centre is the fallback.
                Part best = null;
                DominantBone(kv.Value, out string bn, out int _);
                string want = null;
                if (bn == "head") want = "head";
                else if (bn == "torso" || bn == "pelvis") want = "torso";
                else if (bn != null && (bn.StartsWith("shoulder_") || bn.StartsWith("elbow_")))
                    want = "arm_" + bn.Substring(bn.Length - 1);
                else if (bn != null && (bn.StartsWith("hip_") || bn.StartsWith("knee_") || bn.StartsWith("ankle_")))
                    want = "leg_" + bn.Substring(bn.Length - 1);
                if (want != null) _parts.TryGetValue(want, out best);
                if (best == null)
                {
                    var c = kv.Value.GetAabb().GetCenter();
                    float bd = float.MaxValue;
                    foreach (var part in _parts.Values)
                    {
                        if (!partCenters.TryGetValue(part.Id, out var pc)) continue;
                        float d = c.DistanceSquaredTo(pc);
                        if (d < bd) { bd = d; best = part; }
                    }
                }
                if (best != null)
                {
                    GD.PrintErr($"PARTS {_riderId}: mesh '{kv.Key}' claimed by no layout - attached to {best.Id} by the safety net");
                    AttachMesh(best, kv.Value);
                }
            }
        }
        foreach (var part in _parts.Values)
        {
            string line = $"{_riderId} PART {part.Id} hp={(part.MaxHp > 0 ? part.MaxHp.ToString("F0") : "pool")} meshes=[";
            foreach (var rec in part.Meshes)
            {
                line += $"{rec.Mi.Name}:{rec.BoneName} ";
                part.Volumes.Add(MakeVolume(rec));
            }
            Dbg.Print(line + "]");
        }
        string centers = $"{_riderId} PARTCENTERS root={_walker.RootPos}";
        foreach (var part in _parts.Values)
            centers += $" {part.Id}={PartCenterWorld(part.Id)}";
        Dbg.Print(centers);

        // The fused-miniature builds (ajax + the batch-2 riders) are cut
        // from one shell, so their parts have open seams; with
        // backface culling on, a seam reads as a see-through hole
        // (owner report, 2026-11-01: visible through a
        // rider's gun arm). Render those riders' materials double-sided so a
        // seam shows the part's dark interior instead of the sky.
        if (_riderId == "medea" || _riderId == "demeter" || _riderId == "theseus" || _riderId == "hector" || _riderId == "hermes" || _riderId == "hades" || _riderId == "ares" || _riderId == "amadeus" || _riderId == "apollo")
        {
            var dupCache = new Dictionary<Material, Material>();
            foreach (var part in _parts.Values)
                foreach (var rec in part.Meshes)
                {
                    var mesh = rec.Mi.Mesh;
                    if (mesh == null) continue;
                    for (int si = 0; si < mesh.GetSurfaceCount(); si++)
                    {
                        var mat = rec.Mi.GetSurfaceOverrideMaterial(si) ?? mesh.SurfaceGetMaterial(si);
                        if (mat is not StandardMaterial3D sm) continue;
                        if (!dupCache.TryGetValue(sm, out var dup))
                        {
                            dup = (StandardMaterial3D)sm.Duplicate();
                            ((StandardMaterial3D)dup).CullMode = BaseMaterial3D.CullModeEnum.Disabled;
                            dupCache[sm] = dup;
                        }
                        rec.Mi.SetSurfaceOverrideMaterial(si, dup);
                    }
                }
        }
    }

    // ---------- registry construction ----------

    // See the call in the constructor: duplicate any rider material
    // that multiplies its texture by vertex colours, with the flag
    // off. Materials are shared between meshes, so dedupe by instance.
    private static void StripVertexColorAlbedo(Dictionary<string, MeshInstance3D> byName)
    {
        var done = new Dictionary<Material, Material>();
        foreach (var mi in byName.Values)
        {
            var mesh = mi.Mesh;
            if (mesh == null) continue;
            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                var mat = mesh.SurfaceGetMaterial(s);
                if (mat is not StandardMaterial3D sm || !sm.VertexColorUseAsAlbedo) continue;
                if (!done.TryGetValue(mat, out var repl))
                {
                    repl = (StandardMaterial3D)sm.Duplicate();
                    ((StandardMaterial3D)repl).VertexColorUseAsAlbedo = false;
                    done[mat] = repl;
                }
                mesh.SurfaceSetMaterial(s, repl);
            }
        }
    }

    private static void CollectMeshes(Node n, Dictionary<string, MeshInstance3D> byName)
    {
        if (n is MeshInstance3D mi) byName[mi.Name] = mi;
        foreach (var c in n.GetChildren()) CollectMeshes(c, byName);
    }

    private void AddPart(string id, string display, float hp,
        Dictionary<string, MeshInstance3D> byName, params string[] meshNames)
    {
        var part = new Part { Id = id, Display = display, MaxHp = hp, Hp = hp };
        foreach (var name in meshNames)
        {
            if (!byName.TryGetValue(name, out var mi))
            {
                GD.PrintErr($"{_riderId} PARTS: mesh '{name}' not found for part {id}");
                continue;
            }
            AttachMesh(part, mi);
        }
        _parts[id] = part;
    }

    private void AttachMesh(Part part, MeshInstance3D mi)
    {
        int bone = DominantBone(mi, out string boneName, out int bindIndex);
        part.Meshes.Add(new MeshRec
        {
            Mi = mi,
            OrigParent = mi.GetParent(),
            OrigLocal = mi.Transform,
            OrigSkin = mi.Skin,
            OrigSkeleton = mi.Skeleton,
            Bone = bone,
            BindIndex = bindIndex,
            BoneName = boneName
        });
    }

    // Segmented builds ship their small fragments as separate
    // meshes named Debris_<partId>_N: sweep them into the named
    // part so each fragment breaks and flies on its own. (Only
    // the segmented Hector build uses the convention today.)
    private void AttachDebris(Dictionary<string, MeshInstance3D> byName)
    {
        foreach (var kv in byName)
        {
            if (!kv.Key.StartsWith("Debris_")) continue;
            var rest = kv.Key.Substring("Debris_".Length);
            // The part id is the longest leading segment chain
            // that names a real part: Debris_arm_L_37_small must
            // land on arm_L - stripping only the last segment
            // parsed it to a non-existent "arm_L_37", the bit
            // stayed unclaimed, and since an unclaimed mesh never
            // detaches it hung in the air at the death position
            // after its rider was destroyed (owner, 2026-11-17).
            string cand = rest;
            while (cand.Length > 0 && !_parts.ContainsKey(cand))
            {
                int cut = cand.LastIndexOf('_');
                if (cut <= 0) { cand = ""; break; }
                cand = cand.Substring(0, cut);
            }
            if (cand.Length > 0) AttachMesh(_parts[cand], kv.Value);
        }
    }

    // Dominant skin bone of a mesh from its vertex weights (bind index ->
    // bind name -> skeleton bone index).
    private int DominantBone(MeshInstance3D mi, out string boneName, out int bindIndex)
    {
        boneName = "";
        bindIndex = -1;
        if (mi.Skin == null || mi.Mesh == null) return -1;
        var tally = new Dictionary<int, double>();
        for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
        {
            var arrays = mi.Mesh.SurfaceGetArrays(s);
            var bones = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
            var weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
            int nv = weights.Length / 4;
            for (int v = 0; v < nv; v++)
                for (int k = 0; k < 4; k++)
                {
                    float w = weights[v * 4 + k];
                    if (w > 0.01f)
                    {
                        int b = bones[v * 4 + k];
                        tally[b] = (tally.TryGetValue(b, out var c) ? c : 0) + w;
                    }
                }
        }
        int best = -1; double bv = 0;
        foreach (var kv in tally) if (kv.Value > bv) { bv = kv.Value; best = kv.Key; }
        if (best < 0 || best >= mi.Skin.GetBindCount()) return -1;
        bindIndex = best;
        boneName = mi.Skin.GetBindName(best);
        return _skel.FindBone(boneName);
    }

    private Volume MakeVolume(MeshRec rec)
    {
        var aabb = rec.Mi.GetAabb();
        // A mesh whose dominant bind names a bone the skeleton
        // does not carry (Hermes's skin binds do this) resolves
        // Bone to -1: the volume then comes from the skin's own
        // bind pose - the same transform the detach path below
        // uses - instead of querying the skeleton with -1,
        // which logged an engine error per mesh and left the
        // volume in identity space.
        var inv = rec.Bone >= 0
            ? _skel.GetBoneGlobalRest(rec.Bone).AffineInverse()
            : rec.BindIndex >= 0 && rec.OrigSkin != null
                ? rec.OrigSkin.GetBindPose(rec.BindIndex)
                : Transform3D.Identity;
        Vector3 mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            var c = new Vector3(
                (i & 1) == 0 ? aabb.Position.X : aabb.End.X,
                (i & 2) == 0 ? aabb.Position.Y : aabb.End.Y,
                (i & 4) == 0 ? aabb.Position.Z : aabb.End.Z);
            var b = inv * c;
            mn = mn.Min(b); mx = mx.Max(b);
        }
        return new Volume { Bone = rec.Bone, CenterB = (mn + mx) * 0.5f, HalfB = (mx - mn) * 0.5f };
    }

    // ---------- torso split ----------

    // The kit FBXs merge several logical parts into the single Torso mesh.
    // Split it into region meshes (triangle-centroid classification in
    // the model's own frame: feet at y=0, +x = model left, +z = model front).
    // Regions are tested in order; unmatched triangles keep the core piece.
    // New pieces share the original skin/skeleton and sit at the same tree
    // spot, so rendering is unchanged until a piece is detached.
    private void SplitTorso(Dictionary<string, MeshInstance3D> byName,
        (string Suffix, System.Func<Vector3, bool> Region)[] regions)
    {
        if (!byName.TryGetValue("Torso", out var orig) || orig.Mesh == null) return;
        var parent = orig.GetParent();
        // Classify against the model root, not the walker: both nodes
        // live in the same subtree, so the walker's placement, scale
        // and AABB centring cancel exactly. The walker-relative form
        // drifted whenever parts were rebuilt after the walker had
        // been placed or teleported, and the carve changed with the
        // build timing (Hector's head split, 2026-11-15).
        Node3D modelRoot = null;
        for (Node n = orig; n != null; n = n.GetParent())
            if (n.GetParent() == _walker) { modelRoot = n as Node3D; break; }
        var modelXf = modelRoot != null
            ? modelRoot.GlobalTransform.AffineInverse() * orig.GlobalTransform
            : _walker.GlobalTransform.AffineInverse() * orig.GlobalTransform;
        string baseName = orig.Name;
        var pieces = new Dictionary<string, MeshInstance3D>();
        var triCounts = new Dictionary<string, int>();

        MeshInstance3D Piece(string suffix)
        {
            if (!pieces.TryGetValue(suffix, out var mi))
            {
                mi = new MeshInstance3D
                {
                    Name = baseName + suffix,
                    Skin = orig.Skin,
                    Skeleton = orig.Skeleton,
                    Transform = orig.Transform
                };
                mi.Mesh = new ArrayMesh();
                pieces[suffix] = mi;
                triCounts[suffix] = 0;
            }
            return mi;
        }

        for (int s = 0; s < orig.Mesh.GetSurfaceCount(); s++)
        {
            var arrays = orig.Mesh.SurfaceGetArrays(s);
            var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var idxArr = arrays[(int)Mesh.ArrayType.Index];
            int[] idx;
            if (idxArr.VariantType == Variant.Type.PackedInt32Array)
                idx = idxArr.AsInt32Array();
            else if (idxArr.VariantType == Variant.Type.PackedInt64Array)
            {
                var l = idxArr.AsInt64Array();
                idx = new int[l.Length];
                for (int i = 0; i < l.Length; i++) idx[i] = (int)l[i];
            }
            else
            {
                idx = new int[verts.Length];
                for (int i = 0; i < idx.Length; i++) idx[i] = i;
            }

            // Classify triangles.
            var triRegion = new int[idx.Length / 3];
            for (int t = 0; t < triRegion.Length; t++)
            {
                var c = (verts[idx[t * 3]] + verts[idx[t * 3 + 1]] + verts[idx[t * 3 + 2]]) / 3f;
                var cm = modelXf * c;
                triRegion[t] = regions.Length; // core
                for (int r = 0; r < regions.Length; r++)
                    if (regions[r].Region(cm)) { triRegion[t] = r; break; }
            }

            // Emit one surface per (region) that has triangles.
            var emitted = new Dictionary<int, (Godot.Collections.Array Arr, int[] Idx)>();
            for (int r = 0; r <= regions.Length; r++)
            {
                var remap = new Dictionary<int, int>();
                var srcVerts = new List<int>();
                var newIdx = new List<int>();
                for (int t = 0; t < triRegion.Length; t++)
                {
                    if (triRegion[t] != r) continue;
                    for (int k = 0; k < 3; k++)
                    {
                        int sv = idx[t * 3 + k];
                        if (!remap.TryGetValue(sv, out int nv))
                        {
                            nv = srcVerts.Count;
                            remap[sv] = nv;
                            srcVerts.Add(sv);
                        }
                        newIdx.Add(nv);
                    }
                }
                if (newIdx.Count == 0) continue;
                string suffix = r < regions.Length ? regions[r].Suffix : "Core";
                var mi = Piece(suffix);
                var am = (ArrayMesh)mi.Mesh;
                var outArrays = SliceSurface(arrays, srcVerts, newIdx, verts.Length);
                emitted[r] = (outArrays, newIdx.ToArray());
                int si = am.GetSurfaceCount();
                am.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, outArrays);
                var mat = orig.Mesh.SurfaceGetMaterial(s);
                if (mat != null) am.SurfaceSetMaterial(si, mat);
                triCounts[suffix] += newIdx.Count / 3;
            }

            // Socket plugs (owner, 2026-11-22): a carved pod leaves
            // an open wound in the core - the shell is hollow and
            // single-sided, so once the pod breaks off the player
            // sees straight through the rider. Cap the wound on the
            // CORE piece with a dark plate (see AddSocketPlug).
            for (int r = 0; r < regions.Length; r++)
            {
                if (!regions[r].Suffix.StartsWith("Pod")) continue;
                if (!emitted.TryGetValue(r, out var podE)) continue;
                if (!emitted.TryGetValue(regions.Length, out var coreE)) continue;
                AddSocketPlug((ArrayMesh)Piece("Core").Mesh, coreE.Arr, coreE.Idx, podE.Arr, podE.Idx);
            }
        }

        int insertAt = orig.GetIndex();
        foreach (var kv in pieces)
        {
            parent.AddChild(kv.Value);
            parent.MoveChild(kv.Value, insertAt++);
            byName[kv.Value.Name] = kv.Value;
            Dbg.Print($"{_riderId} SPLIT {kv.Value.Name} tris={triCounts[kv.Key]}");
        }
        byName.Remove(baseName);
        parent.RemoveChild(orig);
        orig.Free();
    }

    private static StandardMaterial3D _socketMat;

    // Cap the wound a carved pod leaves in the core piece. The cut
    // classifies triangles by centroid, so the core's boundary and
    // the chunk's boundary are two different polylines a triangle
    // apart, sharing almost no vertices - the plate therefore fans
    // over the UNION of both rims (only the verts near the chunk;
    // the sculpt's older openings elsewhere on the torso are far
    // away and excluded by the radius test). Each fan vert keeps
    // its own slice's bone data. The plate is an extra surface of
    // the core mesh: hidden inside the pod while it is attached,
    // it stays with the torso when the pod tears away and reads
    // as the empty socket (owner, 2026-11-22).
    private void AddSocketPlug(ArrayMesh coreMesh, Godot.Collections.Array coreArr, int[] coreIdx, Godot.Collections.Array podArr, int[] podIdx)
    {
        var pv = podArr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        if (pv.Length == 0) { Dbg.Print($"{_riderId} SOCKET skip: empty pod slice"); return; }
        var podC = Vector3.Zero;
        foreach (var p in pv) podC += p;
        podC /= pv.Length;
        float podR = 0f;
        foreach (var p in pv) podR = Mathf.Max(podR, (p - podC).Length());

        var rim = new List<(Vector3 Pos, Godot.Collections.Array Arr, int Src, int VertCount)>();
        void Collect(Godot.Collections.Array arr, int[] idx)
        {
            var vs = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var use = new Dictionary<(int, int), int>();
            for (int t = 0; t + 2 < idx.Length; t += 3)
                for (int k = 0; k < 3; k++)
                {
                    int a = idx[t + k], b = idx[t + (k + 1) % 3];
                    var e = a < b ? (a, b) : (b, a);
                    use[e] = use.TryGetValue(e, out int cnt) ? cnt + 1 : 1;
                }
            var seen = new HashSet<int>();
            foreach (var kv in use)
            {
                if (kv.Value != 1) continue;
                foreach (int vi in new[] { kv.Key.Item1, kv.Key.Item2 })
                    if (seen.Add(vi) && (vs[vi] - podC).Length() <= podR + 0.35f)
                        rim.Add((vs[vi], arr, vi, vs.Length));
            }
        }
        Collect(coreArr, coreIdx);
        Collect(podArr, podIdx);
        if (rim.Count < 3) { Dbg.Print($"{_riderId} SOCKET skip: rim {rim.Count} verts"); return; }

        // The two rims pass through nearly the same points; drop
        // near-duplicates or the fan fills with sliver triangles.
        var dedup = new List<(Vector3 Pos, Godot.Collections.Array Arr, int Src, int VertCount)>();
        foreach (var r in rim)
        {
            bool dup = false;
            foreach (var q in dedup) if (q.Pos.DistanceSquaredTo(r.Pos) < 0.0004f) { dup = true; break; }
            if (!dup) dedup.Add(r);
        }
        rim = dedup;
        if (rim.Count < 3) { Dbg.Print($"{_riderId} SOCKET skip: rim {rim.Count} after dedupe"); return; }

        var cen = Vector3.Zero;
        foreach (var r in rim) cen += r.Pos;
        cen /= rim.Count;
        var axis = podC - cen;
        if (axis.Length() < 1e-5f) { Dbg.Print($"{_riderId} SOCKET skip: degenerate axis"); return; }
        axis = axis.Normalized();
        var u = (Mathf.Abs(axis.Y) < 0.9f ? Vector3.Up : Vector3.Right).Cross(axis).Normalized();
        var w = axis.Cross(u);
        rim.Sort((a, b) =>
        {
            var pa = a.Pos - cen; var pb = b.Pos - cen;
            return Mathf.Atan2(pa.Dot(w), pa.Dot(u)).CompareTo(Mathf.Atan2(pb.Dot(w), pb.Dot(u)));
        });

        int n = rim.Count;
        var verts = new Vector3[n + 1];
        for (int i = 0; i < n; i++) verts[i] = rim[i].Pos;
        verts[n] = cen;
        // Normal from the fan's total area vector (robust when
        // individual fan triangles are slivers); zero-area
        // triangles are dropped from the index list.
        var area = Vector3.Zero;
        for (int i = 0; i < n; i++)
            area += (verts[i] - cen).Cross(verts[(i + 1) % n] - cen);
        if (area.Length() < 1e-8f) { Dbg.Print($"{_riderId} SOCKET skip: degenerate fan"); return; }
        var nrm = area.Normalized();
        bool flip = nrm.Dot(axis) < 0f;
        if (flip) nrm = -nrm;
        var idxL = new List<int>();
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            var cr = (verts[i] - cen).Cross(verts[j] - cen);
            if (cr.Length() < 1e-9f) continue;
            idxL.Add(n);
            if (flip) { idxL.Add(j); idxL.Add(i); } else { idxL.Add(i); idxL.Add(j); }
        }
        if (idxL.Count < 3) { Dbg.Print($"{_riderId} SOCKET skip: all fan tris degenerate"); return; }
        var idx = idxL.ToArray();
        var nrms = new Vector3[n + 1];
        for (int i = 0; i <= n; i++) nrms[i] = nrm;

        var arr = new Godot.Collections.Array();
        arr.Resize((int)Mesh.ArrayType.Max);
        arr[(int)Mesh.ArrayType.Vertex] = verts;
        arr[(int)Mesh.ArrayType.Normal] = nrms;
        arr[(int)Mesh.ArrayType.Index] = idx;
        // Skin each fan vert with its own source vert's bone chunk;
        // the centroid takes the first rim vert's.
        foreach (int slot in new[] { (int)Mesh.ArrayType.Bones, (int)Mesh.ArrayType.Weights })
        {
            var o = new float[(n + 1) * 8];
            bool any = false;
            for (int i = 0; i <= n; i++)
            {
                var src = i < n ? rim[i] : rim[0];
                var sv = src.Arr[slot];
                if (sv.VariantType != Variant.Type.PackedFloat32Array) continue;
                var sf = sv.AsFloat32Array();
                int per = sf.Length / src.VertCount;
                if (per <= 0 || per > 8) continue;
                for (int k = 0; k < per; k++) o[i * 8 + k] = sf[src.Src * per + k];
                any = true;
            }
            if (any) arr[slot] = o;
        }
        _socketMat ??= new StandardMaterial3D
        {
            AlbedoColor = new Color(0.05f, 0.05f, 0.06f),
            Roughness = 0.92f,
            Metallic = 0.25f
        };
        int si = coreMesh.GetSurfaceCount();
        coreMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
        coreMesh.SurfaceSetMaterial(si, _socketMat);
        Dbg.Print($"{_riderId} SOCKET plug verts={n}");
    }

    // Copy every attribute slot of a surface, keeping only srcVerts
    // (in order) and the given triangle index list.
    private static Godot.Collections.Array SliceSurface(Godot.Collections.Array arrays,
        List<int> srcVerts, List<int> newIdx, int srcVertCount)
    {
        var outArr = new Godot.Collections.Array();
        outArr.Resize((int)Mesh.ArrayType.Max);
        for (int slot = 0; slot < (int)Mesh.ArrayType.Max; slot++)
        {
            if (slot == (int)Mesh.ArrayType.Index)
            {
                outArr[slot] = newIdx.ToArray();
                continue;
            }
            var v = arrays[slot];
            if (v.VariantType == Variant.Type.Nil) continue;
            outArr[slot] = SliceVariant(v, srcVerts, srcVertCount);
        }
        return outArr;
    }

    // Note: Godot 4.7 C# surface slots carry plain System arrays
    // (Vector3[], float[], int[], ...) under the Packed* Variant types.
    private static Variant SliceVariant(Variant v, List<int> src, int srcCount)
    {
        switch (v.VariantType)
        {
            case Variant.Type.PackedVector3Array:
                {
                    var a = v.AsVector3Array();
                    var o = new Vector3[src.Count];
                    for (int i = 0; i < src.Count; i++) o[i] = a[src[i]];
                    return o;
                }
            case Variant.Type.PackedVector2Array:
                {
                    var a = v.AsVector2Array();
                    var o = new Vector2[src.Count];
                    for (int i = 0; i < src.Count; i++) o[i] = a[src[i]];
                    return o;
                }
            case Variant.Type.PackedColorArray:
                {
                    var a = v.AsColorArray();
                    var o = new Color[src.Count];
                    for (int i = 0; i < src.Count; i++) o[i] = a[src[i]];
                    return o;
                }
            case Variant.Type.PackedFloat32Array:
                {
                    var a = v.AsFloat32Array();
                    int per = srcCount > 0 ? a.Length / srcCount : 0;
                    var o = new float[src.Count * per];
                    for (int i = 0; i < src.Count; i++)
                        for (int k = 0; k < per; k++) o[i * per + k] = a[src[i] * per + k];
                    return o;
                }
            case Variant.Type.PackedInt32Array:
                {
                    var a = v.AsInt32Array();
                    int per = srcCount > 0 ? a.Length / srcCount : 0;
                    var o = new int[src.Count * per];
                    for (int i = 0; i < src.Count; i++)
                        for (int k = 0; k < per; k++) o[i * per + k] = a[src[i] * per + k];
                    return o;
                }
            case Variant.Type.PackedByteArray:
                {
                    var a = v.AsByteArray();
                    int per = srcCount > 0 ? a.Length / srcCount : 0;
                    var o = new byte[src.Count * per];
                    for (int i = 0; i < src.Count; i++)
                        for (int k = 0; k < per; k++) o[i * per + k] = a[src[i] * per + k];
                    return o;
                }
            default:
                return v;
        }
    }

    // ---------- queries ----------

    public bool Has(string id) => _parts.ContainsKey(id);
    public bool PartHasVolumes(string id) => _parts.TryGetValue(id, out var p) && p.Volumes.Count > 0;
    // Tolerant lookups: a chassis may simply not HAVE a part (the
    // Hector has no missile pods). Absent reads as destroyed / 0 HP /
    // null rather than throwing - a hard dictionary lookup here
    // crashed every frame for an Hector player and froze the camera
    // mid-duel (owner's report, 2026-10-30).
    public Part GetPart(string id) => _parts.TryGetValue(id, out var p) ? p : null;
    public bool Alive(string id) => _parts.TryGetValue(id, out var p) && !p.Destroyed;
    public float HpOf(string id) => _parts.TryGetValue(id, out var p) ? p.Hp : 0f;
    public IEnumerable<Part> AllParts => _parts.Values;

    private static float BoxSdf(Vector3 p, Volume v)
    {
        var q = (p - v.CenterB).Abs() - v.HalfB;
        float ox = Mathf.Max(q.X, 0f), oy = Mathf.Max(q.Y, 0f), oz = Mathf.Max(q.Z, 0f);
        float outside = Mathf.Sqrt(ox * ox + oy * oy + oz * oz);
        float inside = Mathf.Min(Mathf.Max(q.X, Mathf.Max(q.Y, q.Z)), 0f);
        return outside + inside;
    }

    // Nearest part (by per-mesh bone-space box SDF) to a world point.
    // Destroyed parts no longer exist on the rider and are skipped.
    public string AttributePoint(Vector3 world)
    {
        string best = "torso";
        float bestSd = float.MaxValue;
        var skelInv = _skel.GlobalTransform.AffineInverse();
        var pSk = skelInv * world;
        var poseInv = new Dictionary<int, Transform3D>();
        foreach (var part in _parts.Values)
        {
            if (part.Destroyed) continue;
            foreach (var vol in part.Volumes)
            {
                if (!poseInv.TryGetValue(vol.Bone, out var inv))
                {
                    inv = _skel.GetBoneGlobalPose(vol.Bone).AffineInverse();
                    poseInv[vol.Bone] = inv;
                }
                float sd = BoxSdf(inv * pSk, vol);
                if (sd < bestSd) { bestSd = sd; best = part.Id; }
            }
        }
        return best;
    }

    // Ray vs the per-part bone-space boxes (slab test in each bone's
    // frame; the affine map preserves the ray parameter). Returns the
    // nearest (t, partId) or t < 0. Destroyed parts are gone and skipped.
    // The TORSO is excluded on purpose: the rider capsule is its hit
    // volume, and its core AABB is a wide slab that would otherwise
    // shadow the shoulder/arm boxes behind its corners.
    public (float T, string Part) RaycastParts(Vector3 origin, Vector3 dir, float maxDist)
    {
        float bestT = -1f;
        string bestPart = "";
        var skelInv = _skel.GlobalTransform.AffineInverse();
        var oSk = skelInv * origin;
        var dSk = skelInv.Basis * dir;
        var poseInv = new Dictionary<int, Transform3D>();
        foreach (var part in _parts.Values)
        {
            if (part.Destroyed || part.Id == "torso") continue;
            foreach (var vol in part.Volumes)
            {
                if (!poseInv.TryGetValue(vol.Bone, out var inv))
                {
                    inv = _skel.GetBoneGlobalPose(vol.Bone).AffineInverse();
                    poseInv[vol.Bone] = inv;
                }
                float t = RayBox(inv * oSk, inv.Basis * dSk, vol);
                if (t > 0.001f && t <= maxDist && (bestT < 0f || t < bestT))
                {
                    bestT = t;
                    bestPart = part.Id;
                }
            }
        }
        return (bestT, bestPart);
    }

    private static float RayBox(Vector3 o, Vector3 d, Volume v)
    {
        float tmin = 0f, tmax = float.MaxValue;
        for (int i = 0; i < 3; i++)
        {
            float lo = v.CenterB[i] - v.HalfB[i];
            float hi = v.CenterB[i] + v.HalfB[i];
            if (Mathf.Abs(d[i]) < 1e-8f)
            {
                if (o[i] < lo || o[i] > hi) return -1f;
            }
            else
            {
                float t1 = (lo - o[i]) / d[i];
                float t2 = (hi - o[i]) / d[i];
                if (t1 > t2) (t1, t2) = (t2, t1);
                if (t1 > tmin) tmin = t1;
                if (t2 < tmax) tmax = t2;
                if (tmax < tmin) return -1f;
            }
        }
        return tmin;
    }

    // Aim point for tests and targeting: the world center of the part's
    // LARGEST volume. The mean of all volume centers can land in empty
    // space for multi-piece parts (head + a thin antenna spike), where
    // attribution would rightly name a different part.
    public Vector3 PartAimPoint(string partId)
    {
        var part = _parts[partId];
        Volume best = null;
        float bestSize = -1f;
        foreach (var vol in part.Volumes)
        {
            float s = vol.HalfB.X * vol.HalfB.Y * vol.HalfB.Z;
            if (s > bestSize) { bestSize = s; best = vol; }
        }
        if (best == null) return _walker.RootPos;
        // Volumes on skeleton-less binds (Bone -1, see
        // MakeVolume) cannot be posed; their centre is the
        // walker's root rather than an identity-pose guess.
        if (best.Bone < 0) return _walker.RootPos;
        return _skel.GlobalTransform * (_skel.GetBoneGlobalPose(best.Bone) * best.CenterB);
    }

    // World-space center of a part (mean of its volume centers).
    public Vector3 PartCenterWorld(string partId)
    {
        var part = _parts[partId];
        if (part.Volumes.Count == 0) return _walker.RootPos;
        var sum = Vector3.Zero;
        var skelG = _skel.GlobalTransform;
        int n = 0;
        foreach (var vol in part.Volumes)
        {
            // Skip skeleton-less binds (Bone -1): posing them
            // logs an engine error and contributes an
            // identity-space centre - worse than leaving the
            // volume out of the mean.
            if (vol.Bone < 0) continue;
            sum += skelG * (_skel.GetBoneGlobalPose(vol.Bone) * vol.CenterB);
            n++;
        }
        return n > 0 ? sum / n : _walker.RootPos;
    }

    // ---------- damage / break-off / debris ----------

    private readonly List<Debris> _debris = new();
    public IReadOnlyList<Debris> DebrisList => _debris;

    // Apply damage to a part's own HP. Returns true when this hit broke
    // the part off. Torso is pool-owned (MaxHp 0) and never breaks here.
    public bool DamagePart(string partId, float dmg)
    {
        var part = _parts[partId];
        if (part.Destroyed || part.MaxHp <= 0f) return false;
        part.Hp = Mathf.Max(part.Hp - dmg, 0f);
        Dbg.Print($"BRK DMG {_riderId} part={partId} dmg={dmg:F1} partHp={part.Hp:F1}/{part.MaxHp:F0}");
        if (part.Hp > 0f) return false;
        var center = PartCenterWorld(partId);
        if (partId == "leg_L" || partId == "leg_R" || partId == "head")
        {
            // Owner's rule: legs NEVER fall off. A destroyed leg stays
            // attached - darkened and deformed - and the rider limps.
            // The head stays on too (owner, 2026-10-31): a shot-off
            // head left the torso a hollow shell you could see
            // through from behind. A wrecked head stays attached,
            // charred and lolling, like the legs.
            WreckPart(part);
            Dbg.Print($"BRK WRECK {_riderId} part={partId} '{part.Display}' (stays attached, rider limps)");
        }
        else
        {
            BreakPart(part, center);
            Dbg.Print($"BRK BREAK {_riderId} part={partId} '{part.Display}' at {center}");
        }
        OnBreak?.Invoke(partId, center);
        return true;
    }

    private static readonly StandardMaterial3D WreckOverlay = new()
    {
        AlbedoColor = new Color(0.05f, 0.04f, 0.03f, 0.62f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        Roughness = 1f
    };

    // The leg-wreck look: a charred translucent overlay darkens the
    // leg's own texture, and the meshes are crushed slightly (shorter,
    // wider) and tilted - deformation without detachment.
    // The wreck deformation (a slight crush and tilt under the
    // char overlay) is applied in each mesh's own space, pivoting
    // on the mesh's own centre. It used to ride on the instance's
    // Scale/Rotation, which pivot on the MODEL ORIGIN at ground
    // level: a wrecked head (centre ~13u up) was scaled ~1u down
    // into the torso and swung ~1u sideways off the neck - "half
    // the head gone, the rest floating" (owner, 2026-11-15) - and
    // wrecked legs sheared away from the pelvis the same way.
    private void WreckPart(Part part)
    {
        part.Destroyed = true;
        part.Wrecked = true;
        float tilt = part.Id == "leg_L" ? 0.085f : -0.085f;
        var rs = Basis.FromEuler(new Vector3(0.045f, 0f, tilt))
            .Scaled(new Vector3(1.07f, 0.92f, 1.07f));
        foreach (var rec in part.Meshes)
        {
            rec.SavedOverlay = rec.Mi.MaterialOverlay;
            var pivot = rec.Mi.Mesh.GetAabb().GetCenter();
            var d = new Transform3D(rs, pivot - rs * pivot);
            rec.Mi.Transform = rec.OrigLocal * d;
            rec.Mi.MaterialOverlay = WreckOverlay;
        }
    }

    private void UnWreck(Part part)
    {
        if (!part.Wrecked) return;
        part.Wrecked = false;
        foreach (var rec in part.Meshes)
        {
            rec.Mi.MaterialOverlay = rec.SavedOverlay;
            rec.Mi.Transform = rec.OrigLocal;
        }
    }

    // Death explosion: EVERY part detaches into debris at once -
    // including the torso core, which normal damage can never break
    // (its MaxHp 0 marks it as the pool itself). OnBreak still fires
    // per part; the owner suppresses the usual consequences while a
    // whole rider is coming apart.
    public void ExplodeAll()
    {
        foreach (var part in _parts.Values)
        {
            if (part.Destroyed && !part.Wrecked) continue;
            if (part.Wrecked) UnWreck(part); // debris piece starts clean
            part.Hp = 0f;
            var center = PartCenterWorld(part.Id);
            BreakPart(part, center);
            OnBreak?.Invoke(part.Id, center);
        }
    }

    // Detach the part's meshes from the skeleton and turn them into
    // falling debris. The skinless-equivalent transform
    // (meshGlobal * bonePose * bindPose) reproduces the skinned vertex
    // positions at the detach instant, so nothing visually jumps; after
    // that the piece is plain scenery under simple gravity.
    private void BreakPart(Part part, Vector3 center)
    {
        part.Destroyed = true;
        var container = new Node3D { Name = $"{_riderId}_Debris_{part.Id}" };
        _walker.GetParent().AddChild(container);
        container.GlobalTransform = new Transform3D(Basis.Identity, center);
        foreach (var rec in part.Meshes)
        {
            var mi = rec.Mi;
            var bind = rec.OrigSkin != null && rec.BindIndex >= 0
                ? rec.OrigSkin.GetBindPose(rec.BindIndex)
                : _skel.GetBoneGlobalRest(rec.Bone).AffineInverse();
            var eff = mi.GlobalTransform * (_skel.GetBoneGlobalPose(rec.Bone) * bind);
            mi.GetParent().RemoveChild(mi);
            container.AddChild(mi);
            mi.GlobalTransform = eff;
            mi.Skin = null;
            mi.Skeleton = new NodePath("");
            rec.Detached = true;
        }
        var away = center - _walker.RootPos;
        away.Y = 0f;
        if (away.LengthSquared() < 0.25f) away = RiderWalker.HeadingVec(_walker.TorsoYawWorld);
        away = away.Normalized();
        var d = new Debris
        {
            Part = part,
            Node = container,
            Vel = away * 2.0f + Vector3.Up * 2.8f,
            AngAxisW = Vector3.Up.Cross(away).Normalized(),
            AngSpeed = 1.6f
        };
        _debris.Add(d);
    }

    public void StepDebris(float dt)
    {
        if (dt <= 0f) return;
        // Substep at 120 Hz like the gait: with a raw frame dt the
        // gravity increment per step (20*dt) can exceed the settle
        // threshold outright - at dt 0.2 a resting piece gains
        // -4 u/s every frame, bounces at 0.22 restitution forever,
        // and never settles (measured: breakable POD phase timing
        // out at slow render rates; the same class of bug as the
        // gait dt clamp found in the terrain stage).
        int n = Mathf.Max(1, Mathf.CeilToInt(dt / (1f / 120f)));
        float h = dt / n;
        for (int i = 0; i < n; i++) StepDebrisStep(h);
    }

    private void StepDebrisStep(float dt)
    {
        foreach (var d in _debris)
        {
            if (d.Settled) continue;
            d.Vel.Y -= 20f * dt;
            d.Node.GlobalPosition += d.Vel * dt;
            if (d.AngSpeed > 0.02f)
            {
                var gt = d.Node.GlobalTransform;
                var nb = new Basis(new Quaternion(d.AngAxisW, d.AngSpeed * dt)) * gt.Basis;
                d.Node.GlobalTransform = new Transform3D(nb, gt.Origin);
            }
            var box = WorldAabb(d.Node);
            // Contact is judged at the AABB's lowest corner: on a slope the
            // downhill corner touches first and is where the piece rests.
            Vector3 low = LowestCorner(box);
            float groundY = Main.HeightAt(low.X, low.Z);
            d.RestMinY = box.Position.Y;
            d.RestGroundY = Main.HeightAt(box.GetCenter().X, box.GetCenter().Z);
            if (low.Y <= groundY + 0.002f)
            {
                d.Node.GlobalPosition += Vector3.Up * (groundY - low.Y);
                d.RestMinY = box.Position.Y + (groundY - low.Y);
                if (d.Vel.Y < -1.6f)
                {
                    d.Vel.Y = -d.Vel.Y * 0.22f; // one bounce
                    d.Vel.X *= 0.55f;
                    d.Vel.Z *= 0.55f;
                    d.AngSpeed *= 0.45f;
                }
                else
                {
                    ConformDebrisToSlope(d);
                    d.Settled = true;
                    d.Vel = Vector3.Zero;
                    d.AngSpeed = 0f;
                    Dbg.Print($"BRK DEBRIS {_riderId} {d.Part.Id} settled pos={d.Node.GlobalPosition} minY={d.RestMinY:F3} groundAtCenter={d.RestGroundY:F3}");
                }
            }
        }
    }

    // Dev audit (gait-shot): the VISUAL clearance of a leg's foot -
    // the lowest skinned vertex of the ankle-bound meshes in the
    // leg part, measured above the terrain under that vertex. The
    // walker's sole point is derived from the ankle's bind-pose
    // projection; this measures what is actually rendered, so a
    // bind/geometry mismatch shows up here even when the sole
    // solver itself is exact.
    public float FootVisualGap(string partId)
    {
        var part = _parts[partId];
        string ankle = partId == "leg_L" ? "ankle_L" : "ankle_R";
        float best = float.MaxValue;
        foreach (var rec in part.Meshes)
        {
            if (rec.Detached || rec.BoneName != ankle || rec.Mi.Mesh == null) continue;
            var bind = rec.OrigSkin != null && rec.BindIndex >= 0
                ? rec.OrigSkin.GetBindPose(rec.BindIndex)
                : _skel.GetBoneGlobalRest(rec.Bone).AffineInverse();
            var eff = rec.Mi.GlobalTransform * (_skel.GetBoneGlobalPose(rec.Bone) * bind);
            for (int s = 0; s < rec.Mi.Mesh.GetSurfaceCount(); s++)
            {
                var verts = rec.Mi.Mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                foreach (var v in verts)
                {
                    var w = eff * v;
                    float gap = w.Y - Main.HeightAt(w.X, w.Z);
                    if (gap < best) best = gap;
                }
            }
        }
        return best;
    }

    // Dev audit (gait-shot): the same visual clearance split by
    // foot end - the lowest toe-side vertex (forward of the ankle)
    // vs the lowest heel-side vertex. A foot can touch at the toe
    // (gap 0) while standing pitched on its toes with the heel
    // high; the split reads that pitch as two numbers.
    public (float Toe, float Heel) FootPitchGaps(string partId, Vector3 ankleW, Vector3 fwdW)
    {
        var part = _parts[partId];
        string ankle = partId == "leg_L" ? "ankle_L" : "ankle_R";
        float toe = float.MaxValue, heel = float.MaxValue;
        foreach (var rec in part.Meshes)
        {
            if (rec.Detached || rec.BoneName != ankle || rec.Mi.Mesh == null) continue;
            var bind = rec.OrigSkin != null && rec.BindIndex >= 0
                ? rec.OrigSkin.GetBindPose(rec.BindIndex)
                : _skel.GetBoneGlobalRest(rec.Bone).AffineInverse();
            var eff = rec.Mi.GlobalTransform * (_skel.GetBoneGlobalPose(rec.Bone) * bind);
            for (int s = 0; s < rec.Mi.Mesh.GetSurfaceCount(); s++)
            {
                var verts = rec.Mi.Mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                foreach (var v in verts)
                {
                    var w = eff * v;
                    float fwd = (w - ankleW).Dot(fwdW);
                    if (Mathf.Abs(fwd) < 0.5f) continue;
                    float gap = w.Y - Main.HeightAt(w.X, w.Z);
                    if (fwd > 0f) { if (gap < toe) toe = gap; }
                    else if (gap < heel) heel = gap;
                }
            }
        }
        return (toe, heel);
    }

    public Debris DebrisOf(string partId)
    {
        foreach (var d in _debris) if (d.Part.Id == partId) return d;
        return null;
    }

    // Resting check for the harness: the part's debris is settled and its
    // world AABB's lowest corner sits on the terrain sampled at that
    // corner (the physically meaningful contact on a slope).
    public bool DebrisRestOk(string partId, float tol, out float minY, out float groundY)
    {
        minY = 0f; groundY = 0f;
        var d = DebrisOf(partId);
        if (d == null || !d.Settled) return false;
        var box = WorldAabb(d.Node);
        var low = LowestCorner(box);
        minY = low.Y;
        groundY = Main.HeightAt(low.X, low.Z);
        return Mathf.Abs(low.Y - groundY) <= tol;
    }

    // Rebind every detached mesh to the skeleton and refill all part HP.
    // Safe to call repeatedly and mid-flight.
    public void RestoreAll()
    {
        foreach (var d in _debris)
        {
            foreach (var rec in d.Part.Meshes)
            {
                var mi = rec.Mi;
                mi.GetParent()?.RemoveChild(mi);
                rec.OrigParent.AddChild(mi);
                mi.Transform = rec.OrigLocal;
                mi.Skin = rec.OrigSkin;
                mi.Skeleton = rec.OrigSkeleton;
                rec.Detached = false;
            }
            d.Node.QueueFree();
        }
        _debris.Clear();
        foreach (var part in _parts.Values)
        {
            if (part.Wrecked) UnWreck(part);
            part.Hp = part.MaxHp;
            part.Destroyed = false;
        }
    }

    private static Vector3 CornerOf(Aabb box, int i) => new Vector3(
        (i & 1) == 0 ? box.Position.X : box.End.X,
        (i & 2) == 0 ? box.Position.Y : box.End.Y,
        (i & 4) == 0 ? box.Position.Z : box.End.Z);

    private static Vector3 LowestCorner(Aabb box)
    {
        Vector3 best = box.Position;
        float bestY = float.MaxValue;
        for (int i = 0; i < 8; i++)
        {
            var c = CornerOf(box, i);
            if (c.Y < bestY) { bestY = c.Y; best = c; }
        }
        return best;
    }

    // A piece settles at its first AABB-corner touch, frozen in
    // whatever tumble orientation it had: on a steep slope the
    // rest of the piece hangs in the air over the downhill side
    // (owner, 2026-11-22: a destroyed enemy floated above the
    // snow). On steep ground only, rotate the settled piece
    // about its contact corner until world down meets the
    // terrain normal, then re-seat it: lift out of any corner
    // penetration, drop the lowest corner back onto its own
    // ground. Gentle ground keeps the original rest untouched.
    private void ConformDebrisToSlope(Debris d)
    {
        var box = WorldAabb(d.Node);
        var centre = box.GetCenter();
        float e = 2.0f;
        float hx = (Main.HeightAt(centre.X + e, centre.Z) - Main.HeightAt(centre.X - e, centre.Z)) / (2f * e);
        float hz = (Main.HeightAt(centre.X, centre.Z + e) - Main.HeightAt(centre.X, centre.Z - e)) / (2f * e);
        var n = new Vector3(-hx, 1f, -hz).Normalized();
        if (n.Y > 0.94f) return; // under ~20 degrees the corner rest reads fine
        var low = LowestCorner(box);
        var axis = Vector3.Up.Cross(n);
        if (axis.LengthSquared() < 1e-8f) return;
        float ang = Mathf.Acos(Mathf.Clamp(Vector3.Up.Dot(n), -1f, 1f));
        var q = new Quaternion(axis.Normalized(), ang);
        var gt = d.Node.GlobalTransform;
        d.Node.GlobalTransform = new Transform3D(new Basis(q) * gt.Basis, low + q * (gt.Origin - low));
        box = WorldAabb(d.Node);
        float lift = 0f;
        for (int i = 0; i < 8; i++)
        {
            var c = CornerOf(box, i);
            lift = Mathf.Max(lift, Main.HeightAt(c.X, c.Z) - c.Y);
        }
        if (lift > 0f) { d.Node.GlobalPosition += Vector3.Up * lift; box = WorldAabb(d.Node); }
        low = LowestCorner(box);
        float drop = low.Y - Main.HeightAt(low.X, low.Z);
        if (Mathf.Abs(drop) > 0.001f) d.Node.GlobalPosition -= Vector3.Up * drop;
        var fb = WorldAabb(d.Node);
        d.RestMinY = fb.Position.Y;
        d.RestGroundY = Main.HeightAt(fb.GetCenter().X, fb.GetCenter().Z);
    }

    private static Aabb WorldAabb(Node node)
    {
        var box = new Aabb();
        bool first = true;
        CollectAabb(node, ref box, ref first);
        return box;
    }

    private static void CollectAabb(Node node, ref Aabb box, ref bool first)
    {
        if (node is MeshInstance3D mi)
        {
            var local = mi.GetAabb();
            var gt = mi.GlobalTransform;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? local.Position.X : local.End.X,
                    (i & 2) == 0 ? local.Position.Y : local.End.Y,
                    (i & 4) == 0 ? local.Position.Z : local.End.Z);
                var w = gt * corner;
                if (first) { box = new Aabb(w, Vector3.Zero); first = false; }
                else box = box.Expand(w);
            }
        }
        foreach (var child in node.GetChildren()) CollectAabb(child, ref box, ref first);
    }
}
