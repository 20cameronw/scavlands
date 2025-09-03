// Designed by KINEMATION, 2024.

using KINEMATION.FPSAnimationFramework.Runtime.Core;
using KINEMATION.FPSAnimationFramework.Runtime.Layers.WeaponLayer;
using KINEMATION.KAnimationCore.Runtime.Core;

using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using System.Reflection;

namespace KINEMATION.FPSAnimationFramework.Runtime.Layers.AdditiveLayer
{
    public struct AdditiveLayerJob : IAnimationJob, IAnimationLayerJob
    {
        public KTransform recoil;
        public LayerJobData jobData;
        public float aimWeight;
        public float curveAimScale;

        private TransformStreamHandle _weaponBoneHandle;
        private TransformStreamHandle _additiveBoneHandle;

        private AdditiveLayerSettings _settings;

        // Decoupled: no compile-time reference to RecoilAnimation
        private Object _recoilComponent;           // MonoBehaviour with OutRot/OutLoc
        private PropertyInfo _propOutRot;          // Quaternion
        private PropertyInfo _propOutLoc;          // Vector3

        private KTransform _ikMotion;

        private float _curveSmoothing;
        private int _aimingWeightPropertyIndex;

        private WeaponLayerJobData _weaponJobData;

        public void ProcessAnimation(AnimationStream stream)
        {
            if (!KAnimationMath.IsWeightRelevant(jobData.weight)) return;

            _curveSmoothing = Mathf.Approximately(_settings.interpSpeed, 0f)
                ? 1f
                : KMath.ExpDecayAlpha(_settings.interpSpeed, stream.deltaTime);

            _weaponJobData.Cache(stream);

            AnimLayerJobUtility.MoveInSpace(stream, _weaponBoneHandle, _weaponBoneHandle, recoil.position, jobData.weight);
            AnimLayerJobUtility.RotateInSpace(stream, _weaponBoneHandle, _weaponBoneHandle, recoil.rotation, jobData.weight);

            var additive = AnimLayerJobUtility.GetTransformFromHandle(stream, _additiveBoneHandle, false);
            _ikMotion = KTransform.Lerp(_ikMotion, additive, _curveSmoothing);

            AnimLayerJobUtility.MoveInSpace(stream, jobData.rootHandle, _weaponBoneHandle, _ikMotion.position, jobData.weight * curveAimScale);
            AnimLayerJobUtility.RotateInSpace(stream, jobData.rootHandle, _weaponBoneHandle, _ikMotion.rotation, jobData.weight * curveAimScale);

            _weaponJobData.PostProcessPose(stream, jobData.weight);
        }

        public void ProcessRootMotion(AnimationStream stream) {}

        public void Initialize(LayerJobData newJobData, FPSAnimatorLayerSettings settings)
        {
            _settings = (AdditiveLayerSettings)settings;

            jobData = newJobData;
            _weaponJobData = new WeaponLayerJobData();
            _weaponJobData.Setup(jobData, _settings);

            _aimingWeightPropertyIndex = jobData.inputController.GetPropertyIndex(_settings.aimingInputProperty);

            recoil = KTransform.Identity;

            Transform ikWeaponBone = jobData.rigComponent.GetRigTransform(_settings.weaponIkBone);
            _weaponBoneHandle = jobData.animator.BindStreamTransform(ikWeaponBone);

            var additiveBone = jobData.rigComponent.GetRigTransform(_settings.additiveBone);
            _additiveBoneHandle = jobData.animator.BindStreamTransform(additiveBone);

            // ---- Lazy-find a component that exposes OutRot (Quaternion) & OutLoc (Vector3)
            var animatorGO = jobData.animator.gameObject;
            var behaviours = animatorGO.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                var t = behaviours[i].GetType();
                var rot = t.GetProperty("OutRot", BindingFlags.Public | BindingFlags.Instance);
                var loc = t.GetProperty("OutLoc", BindingFlags.Public | BindingFlags.Instance);
                if (rot != null && loc != null &&
                    rot.PropertyType == typeof(Quaternion) &&
                    loc.PropertyType == typeof(Vector3))
                {
                    _recoilComponent = behaviours[i];
                    _propOutRot = rot;
                    _propOutLoc = loc;
                    break;
                }
            }
        }

        public AnimationScriptPlayable CreatePlayable(PlayableGraph graph)
        {
            return AnimationScriptPlayable.Create(graph, this);
        }

        public FPSAnimatorLayerSettings GetSettingAsset() => _settings;

        public void OnLayerLinked(FPSAnimatorLayerSettings newSettings) {}

        public void UpdateEntity(FPSAnimatorEntity newEntity) {}

        public void OnPreGameThreadUpdate() {}

        public void UpdatePlayableJobData(AnimationScriptPlayable playable, float weight)
        {
            var job = playable.GetJobData<AdditiveLayerJob>();

            job.jobData.weight = weight;
            job.aimWeight = jobData.inputController.GetValue<float>(_aimingWeightPropertyIndex);
            job.curveAimScale = Mathf.Lerp(1f, _settings.adsScalar, job.aimWeight);

            // Read via cached reflection (no hard dependency)
            if (_recoilComponent != null && _propOutRot != null && _propOutLoc != null)
            {
                var rot = (Quaternion)_propOutRot.GetValue(_recoilComponent);
                var pos = (Vector3)_propOutLoc.GetValue(_recoilComponent);
                job.recoil.rotation = rot;
                job.recoil.position = pos;
            }

            playable.SetJobData(job);
        }

        public void LateUpdate() {}

        public void Destroy() {}
    }
}
