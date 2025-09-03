// Designed by KINEMATION, 2025.

using System;
using System.Collections.Generic;
using KINEMATION.KAnimationCore.Runtime.Core;
using UnityEngine;
using Random = UnityEngine.Random;

namespace KINEMATION.ProceduralRecoilAnimationSystem.Runtime
{
    public enum FireMode { Semi, Burst, Auto }

    public struct StartRest
    {
        public StartRest(bool x, bool y, bool z) { this.x = x; this.y = y; this.z = z; }
        public bool x, y, z;
    }

    public delegate bool ConditionDelegate();
    public delegate void PlayDelegate();
    public delegate void StopDelegate();

    public struct AnimState
    {
        public ConditionDelegate checkCondition;
        public PlayDelegate onPlay;
        public StopDelegate onStop;
    }

    // ---- Replaced ScriptableObject with a plain serializable profile you set per-weapon ----
    [Serializable]
    public struct ScalarProgress
    {
        public float amount;        // per-shot add
        public float acceleration;  // rise speed
        public float damping;       // fall speed
    }

    [Serializable]
    public struct RecoilSwayProfile
    {
        public Vector2 pitchSway;   // random per-shot range
        public Vector2 yawSway;     // random per-shot range
        public float   adsScale;    // ADS multiplier
        public float   acceleration;
        public float   damping;
        public float   rollMultiplier;
        public Vector3 pivotOffset;
    }

    [Serializable]
    public struct RecoilCurves
    {
        public VectorCurve semiRotCurve;
        public VectorCurve semiLocCurve;
        public VectorCurve autoRotCurve;
        public VectorCurve autoLocCurve;

        public static float GetMaxTime(AnimationCurve curve) => curve[curve.length - 1].time;

        public RecoilCurves(Keyframe[] kf)
        {
            semiRotCurve = new VectorCurve(kf);
            semiLocCurve = new VectorCurve(kf);
            autoRotCurve = new VectorCurve(kf);
            autoLocCurve = new VectorCurve(kf);
        }
    }

    [Serializable]
    public class RecoilProfile
    {
        [Header("Playback")]
        public float playRate = 1f;            // how fast the curves play
        public bool  smoothRoll = false;

        [Header("Curves")]
        public RecoilCurves recoilCurves;

        [Header("Offsets")]
        public Vector3 hipPivotOffset = Vector3.zero;
        public Vector3 aimPivotOffset = Vector3.zero;

        [Header("ADS Multipliers")]
        public Vector3 aimRot = Vector3.one;   // x=pitch, y=yaw, z=roll scale while ADS
        public Vector3 aimLoc = Vector3.one;   // x=right, y=up, z=back scale while ADS

        [Header("Curve Smoothing (Exp-Decay)")]
        public Vector3 smoothRot = new Vector3(10,10,10);
        public Vector3 smoothLoc = new Vector3(10,10,10);
        public Vector3 extraRot  = Vector3.one;  // scale after smoothing
        public Vector3 extraLoc  = Vector3.one;

        [Header("Noise (random, additive to loc x/y)")]
        public Vector2 noiseX = Vector2.zero;
        public Vector2 noiseY = Vector2.zero;
        public Vector2 noiseDamp  = new Vector2(8, 8);
        public Vector2 noiseAccel = new Vector2(30, 30);
        public float   noiseScalar = 1f; // ADS scaling factor

        [Header("Pushback (loc z)")]
        public float pushAmount = 0.0f;
        public float pushDamp   = 8f;
        public float pushAccel  = 30f;

        [Header("Progression (Cumulative help to climb)")]
        public ScalarProgress pitchProgress;  // affects rotation.x
        public ScalarProgress upProgress;     // affects translation.y

        [Header("Recoil Sway (tiny view kick)")]
        public RecoilSwayProfile recoilSway = new RecoilSwayProfile {
            pitchSway = new Vector2(0,0),
            yawSway   = new Vector2(0,0),
            adsScale  = 1f,
            acceleration = 8f,
            damping = 8f,
            rollMultiplier = 0.0f,
            pivotOffset = Vector3.zero
        };
    }

    // Deterministic per-shot pattern asset (Rust-like)
    [CreateAssetMenu(fileName = "RecoilPattern", menuName = "Recoil/Pattern Asset")]
    public class RecoilPatternAsset : ScriptableObject
    {
        [Header("Hipfire pattern (degrees per shot)   x=pitch up, y=yaw right")]
        public Vector2[] hip;
        [Header("ADS pattern (optional)")]
        public Vector2[] ads;
        [Header("Per-shot local kick (x=right,y=up,z=back; optional)")]
        public Vector3[] loc;
        public Vector3 defaultKickPerShot = new Vector3(0f, 0f, 0.01f);

        [Header("Playback")]
        public bool loop = true;
        public bool pingPong = false; // simple bounce

        public int Length(bool isADS)
        {
            var arr = (isADS && ads != null && ads.Length > 0) ? ads : hip;
            return (arr != null) ? arr.Length : 0;
        }

        public Vector2 GetRotShot(int idx, bool isADS)
        {
            var arr = (isADS && ads != null && ads.Length > 0) ? ads : hip;
            if (arr == null || arr.Length == 0) return Vector2.zero;
            idx = Mathf.Clamp(idx, 0, arr.Length - 1);
            return arr[idx];
        }

        public Vector3 GetLocShot(int idx)
        {
            if (loc == null || loc.Length == 0) return defaultKickPerShot;
            idx = Mathf.Clamp(idx, 0, loc.Length - 1);
            return loc[idx];
        }

        public int NextIndex(int current, bool forward, bool isADS)
        {
            int n = Length(isADS);
            if (n <= 0) return 0;
            if (!forward) return Mathf.Max(0, current - 1);

            int next = current + 1;
            if (pingPong)
            {
                if (next >= n) next = n - 2 >= 0 ? n - 2 : 0;
                return next;
            }
            if (loop) { if (next >= n) next = 0; return next; }
            return Mathf.Min(next, n - 1);
        }
    }

    [HelpURL("https://kinemation.gitbook.io/scriptable-animation-system/recoil-system/recoil-animation")]
    public class RecoilAnimation : MonoBehaviour
    {
        public Quaternion OutRot { get; private set; }
        public Vector3    OutLoc { get; private set; }

        public bool isAiming;

        // NEW: no RecoilAnimData; we use a plain profile
        public RecoilProfile Profile { get; private set; }

        private float _fireRate;
        public FireMode fireMode;

        private List<AnimState> _stateMachine;
        private int _stateIndex;

        private Vector3 _targetRot, _targetLoc;
        private VectorCurve _tempRotCurve, _tempLocCurve;
        private Vector3 _startValRot, _startValLoc;
        private StartRest _canRestRot, _canRestLoc;
        private Vector3 _rawRotOut, _rawLocOut, _smoothRotOut, _smoothLocOut;
        private Vector2 _noiseTarget, _noiseOut;
        private float _pushTarget, _pushOut;

        private float _lastFrameTime, _playBack, _lastTimeShot;
        private bool  _isPlaying, _isLooping, _enableSmoothing;

        private Vector2 _pitchSway, _yawSway;
        private Vector2 _pitchProgress, _upProgress;

        // Deterministic pattern (Rust-like)
        [Header("Pattern Mode (Rust-like)")]
        public bool useDeterministicPattern = true;
        public RecoilPatternAsset pattern;
        [Range(0.1f, 3f)] public float patternScale = 1f;
        public bool resetPatternOnStop = true;
        public bool deterministicZeroNoise = true;
        public bool deterministicZeroSway  = false;
        private int _patternIndex;

        // ---------- API ----------
        public void Init(RecoilProfile profile, float fireRate, FireMode newFireMode)
        {
            Profile = profile;
            fireMode = newFireMode;

            OutRot = Quaternion.identity;
            OutLoc = Vector3.zero;

            if (Mathf.Approximately(fireRate, 0f))
            {
                _fireRate = 0.001f;
                Debug.LogWarning("RecoilAnimation: FireRate is zero!");
            }
            else _fireRate = fireRate;

            _targetRot = Vector3.zero;
            _targetLoc = Vector3.zero;

            _pushTarget = 0f;
            _noiseTarget = Vector2.zero;

            _patternIndex = 0;
            SetupStateMachine();
        }

        public void Play()
        {
            if (Profile == null) return;

            for (int i = 0; i < _stateMachine.Count; i++)
            {
                if (_stateMachine[i].checkCondition.Invoke()) { _stateIndex = i; break; }
            }
            _stateMachine[_stateIndex].onPlay.Invoke();
            _lastTimeShot = Time.unscaledTime;
        }

        public void Stop()
        {
            if (Profile == null) return;

            _stateMachine[_stateIndex].onStop.Invoke();
            _isLooping = false;
            if (resetPatternOnStop) _patternIndex = 0;
        }

        private void Update()
        {
            if (Profile == null) return;

            if (_isPlaying) { UpdateSolver(); UpdateTimeline(); }

            Vector3 finalLoc = _smoothLocOut;
            Vector3 finalEulerRot = _smoothRotOut;

            ApplyNoise(ref finalLoc);
            ApplyPushback(ref finalLoc);
            ApplyProgression(ref finalLoc, ref finalEulerRot);

            Quaternion finalRot = Quaternion.Euler(finalEulerRot);
            Vector3 pivotOffset = isAiming ? Profile.aimPivotOffset : Profile.hipPivotOffset;
            finalLoc += finalRot * pivotOffset - pivotOffset;

            ApplySway(ref finalLoc, ref finalRot);

            OutRot = finalRot;
            OutLoc = finalLoc;
        }

        // ---------- Core ----------
        private void CalculateTargetData()
        {
            // Deterministic pattern path
            if (useDeterministicPattern && pattern != null && pattern.Length(isAiming) > 0)
            {
                Vector2 rot = pattern.GetRotShot(_patternIndex, isAiming) * patternScale;
                Vector3 loc = pattern.GetLocShot(_patternIndex);

                float aimPitch = isAiming ? Profile.aimRot.x : 1f;
                float aimYaw   = isAiming ? Profile.aimRot.y : 1f;
                float aimRoll  = isAiming ? Profile.aimRot.z : 1f;
                float aimLocX  = isAiming ? Profile.aimLoc.x : 1f;
                float aimLocY  = isAiming ? Profile.aimLoc.y : 1f;
                float aimLocZ  = isAiming ? Profile.aimLoc.z : 1f;

                _targetRot = new Vector3(rot.x * aimPitch, rot.y * aimYaw, 0f * aimRoll);
                _targetLoc = new Vector3(loc.x * aimLocX,  loc.y * aimLocY,  loc.z * aimLocZ);

                if (deterministicZeroNoise) { _noiseTarget = Vector2.zero; _noiseOut = Vector2.zero; }
                _pushTarget = Profile.pushAmount;

                float adsScalar = isAiming ? Profile.playRate /* reuse if you like */ : 1f;
                _pitchProgress.y += Profile.pitchProgress.amount * (isAiming ? 1f : 1f);
                _upProgress.y    += Profile.upProgress.amount   * (isAiming ? 1f : 1f);

                if (deterministicZeroSway)
                {
                    _pitchSway.y = 0f; _yawSway.y = 0f;
                }
                else
                {
                    var s = Profile.recoilSway;
                    float v = Random.Range(s.pitchSway.x, s.pitchSway.y); if (isAiming) v *= s.adsScale; _pitchSway.y += v;
                    v = Random.Range(s.yawSway.x, s.yawSway.y);           if (isAiming) v *= s.adsScale; _yawSway.y   += v;
                }

                _patternIndex = pattern.NextIndex(_patternIndex, true, isAiming);
                return;
            }

            // Fallback random style (kept so profile can still behave like old data if you want)
            float pitch = Random.Range(0f, 0f);
            float yaw   = 0f;
            float roll  = 0f;

            float kick = 0f, kickRight = 0f, kickUp = 0f;

            _targetRot = new Vector3(pitch, yaw, roll);
            _targetLoc = new Vector3(kickRight, kickUp, kick);

            _pitchProgress.y += Profile.pitchProgress.amount;
            _upProgress.y    += Profile.upProgress.amount;
        }

        private void UpdateTimeline()
        {
            _playBack += Time.deltaTime * Profile.playRate;
            _playBack = Mathf.Clamp(_playBack, 0f, _lastFrameTime);

            if (Mathf.Approximately(_playBack, _lastFrameTime))
            {
                if (_isLooping) { _playBack = 0f; _isPlaying = true; }
                else { _isPlaying = false; _playBack = 0f; }
            }
        }

        private void UpdateSolver()
        {
            if (Mathf.Approximately(_playBack, 0f)) { CalculateTargetData(); }

            float lastPlayback = Mathf.Max(_playBack - Time.deltaTime * Profile.playRate, 0f);

            Vector3 alpha = _tempRotCurve.GetValue(_playBack);
            Vector3 lastAlpha = _tempRotCurve.GetValue(lastPlayback);

            Vector3 output = Vector3.zero;
            output.x = Mathf.LerpUnclamped(CorrectStart(ref lastAlpha.x, alpha.x, ref _canRestRot.x, ref _startValRot.x), _targetRot.x, alpha.x);
            output.y = Mathf.LerpUnclamped(CorrectStart(ref lastAlpha.y, alpha.y, ref _canRestRot.y, ref _startValRot.y), _targetRot.y, alpha.y);
            output.z = Mathf.LerpUnclamped(CorrectStart(ref lastAlpha.z, alpha.z, ref _canRestRot.z, ref _startValRot.z), _targetRot.z, alpha.z);
            _rawRotOut = output;

            alpha = _tempLocCurve.GetValue(_playBack);
            lastAlpha = _tempLocCurve.GetValue(lastPlayback);

            output.x = Mathf.LerpUnclamped(CorrectStart(ref lastAlpha.x, alpha.x, ref _canRestLoc.x, ref _startValLoc.x), _targetLoc.x, alpha.x);
            output.y = Mathf.LerpUnclamped(CorrectStart(ref lastAlpha.y, alpha.y, ref _canRestLoc.y, ref _startValLoc.y), _targetLoc.y, alpha.y);
            output.z = Mathf.LerpUnclamped(CorrectStart(ref lastAlpha.z, alpha.z, ref _canRestLoc.z, ref _startValLoc.z), _targetLoc.z, alpha.z);
            _rawLocOut = output;

            ApplySmoothing();
        }

        private void ApplySmoothing()
        {
            if (_enableSmoothing)
            {
                Vector3 lerped = _smoothRotOut;
                Vector3 smooth = Profile.smoothRot;

                float Interp(float a, float b, float speed, float scale)
                {
                    scale = Mathf.Approximately(scale, 0f) ? 1f : scale;
                    return Mathf.Approximately(speed, 0f) ? b * scale : Mathf.Lerp(a, b * scale, KMath.ExpDecayAlpha(speed, Time.deltaTime));
                }

                lerped.x = Interp(_smoothRotOut.x, _rawRotOut.x, smooth.x, Profile.extraRot.x);
                lerped.y = Interp(_smoothRotOut.y, _rawRotOut.y, smooth.y, Profile.extraRot.y);
                lerped.z = Interp(_smoothRotOut.z, _rawRotOut.z, smooth.z, Profile.extraRot.z);
                _smoothRotOut = lerped;

                lerped = _smoothLocOut; smooth = Profile.smoothLoc;
                lerped.x = Interp(_smoothLocOut.x, _rawLocOut.x, smooth.x, Profile.extraLoc.x);
                lerped.y = Interp(_smoothLocOut.y, _rawLocOut.y, smooth.y, Profile.extraLoc.y);
                lerped.z = Interp(_smoothLocOut.z, _rawLocOut.z, smooth.z, Profile.extraLoc.z);
                _smoothLocOut = lerped;
            }
            else
            {
                _smoothRotOut = _rawRotOut;
                _smoothLocOut = _rawLocOut;
            }
        }

        private void ApplyNoise(ref Vector3 finalized)
        {
            if (useDeterministicPattern && deterministicZeroNoise) return;

            _noiseTarget.x = Mathf.Lerp(_noiseTarget.x, 0f, KMath.ExpDecayAlpha(Profile.noiseDamp.x, Time.deltaTime));
            _noiseTarget.y = Mathf.Lerp(_noiseTarget.y, 0f, KMath.ExpDecayAlpha(Profile.noiseDamp.y, Time.deltaTime));

            _noiseOut.x = Mathf.Lerp(_noiseOut.x, _noiseTarget.x, KMath.ExpDecayAlpha(Profile.noiseAccel.x, Time.deltaTime));
            _noiseOut.y = Mathf.Lerp(_noiseOut.y, _noiseTarget.y, KMath.ExpDecayAlpha(Profile.noiseAccel.y, Time.deltaTime));

            finalized += new Vector3(_noiseOut.x, _noiseOut.y, 0f);
        }

        private void ApplyPushback(ref Vector3 finalized)
        {
            _pushTarget = Mathf.Lerp(_pushTarget, 0f, KMath.ExpDecayAlpha(Profile.pushDamp, Time.deltaTime));
            _pushOut = Mathf.Lerp(_pushOut, _pushTarget, KMath.ExpDecayAlpha(Profile.pushAccel, Time.deltaTime));
            finalized += new Vector3(0f, 0f, _pushOut);
        }

        private void ApplyProgression(ref Vector3 translation, ref Vector3 rotation)
        {
            float a = KMath.ExpDecayAlpha(Profile.pitchProgress.acceleration, Time.deltaTime);
            _pitchProgress.x = Mathf.Lerp(_pitchProgress.x, _pitchProgress.y, a);

            a = KMath.ExpDecayAlpha(Profile.upProgress.acceleration, Time.deltaTime);
            _upProgress.x = Mathf.Lerp(_upProgress.x, _upProgress.y, a);

            a = KMath.ExpDecayAlpha(Profile.pitchProgress.damping, Time.deltaTime);
            _pitchProgress.y = Mathf.Lerp(_pitchProgress.y, 0f, a);

            a = KMath.ExpDecayAlpha(Profile.upProgress.damping, Time.deltaTime);
            _upProgress.y = Mathf.Lerp(_upProgress.y, 0f, a);

            translation.y += _upProgress.x;
            rotation.x    += _pitchProgress.x;
        }

        private void ApplySway(ref Vector3 translation, ref Quaternion rotation)
        {
            if (useDeterministicPattern && deterministicZeroSway) return;

            var s = Profile.recoilSway;

            float a = KMath.ExpDecayAlpha(s.acceleration, Time.deltaTime);
            _pitchSway.x = Mathf.Lerp(_pitchSway.x, _pitchSway.y, a);
            _yawSway.x   = Mathf.Lerp(_yawSway.x,   _yawSway.y, a);

            a = KMath.ExpDecayAlpha(s.damping, Time.deltaTime);
            _pitchSway.y = Mathf.Lerp(_pitchSway.y, 0f, a);
            _yawSway.y   = Mathf.Lerp(_yawSway.y,   0f, a);

            Quaternion swayRot = Quaternion.Euler(new Vector3(_pitchSway.x, _yawSway.x, _yawSway.x * s.rollMultiplier));
            Vector3    swayPos = swayRot * s.pivotOffset - s.pivotOffset;

            rotation *= swayRot;
            translation += swayPos;
        }

        private float CorrectStart(ref float last, float current, ref bool bStartRest, ref float startVal)
        {
            if (Mathf.Abs(last) > Mathf.Abs(current) && bStartRest && !_isLooping) { startVal = 0f; bStartRest = false; }
            last = current; return startVal;
        }

        private void SetupStateMachine()
        {
            _stateMachine ??= new List<AnimState>();
            _stateMachine.Clear();

            AnimState semiState;
            AnimState autoState;

            semiState.checkCondition = () =>
            {
                float timerError = (60f / _fireRate) / Time.deltaTime + 1;
                timerError *= Time.deltaTime;
                if (_enableSmoothing && !_isLooping) _enableSmoothing = false;
                return GetDelta() > timerError + 0.01f && !_isLooping || fireMode == FireMode.Semi;
            };

            semiState.onPlay = () =>
            {
                SetupTransition(_smoothRotOut, _smoothLocOut, Profile.recoilCurves.semiRotCurve, Profile.recoilCurves.semiLocCurve);
            };
            semiState.onStop = () => { };

            autoState.checkCondition = () => true;

            autoState.onPlay = () =>
            {
                if (_isLooping) return;

                var curves = Profile.recoilCurves;
                bool valid = curves.autoRotCurve.IsValid() && curves.autoLocCurve.IsValid();

                _enableSmoothing = valid;
                float correction = 60f / _fireRate;

                if (valid)
                {
                    CorrectAlpha(curves.autoRotCurve, curves.autoLocCurve, correction);
                    SetupTransition(_startValRot, _startValLoc, curves.autoRotCurve, curves.autoLocCurve);
                }
                else if (curves.semiRotCurve.IsValid() && curves.semiLocCurve.IsValid())
                {
                    CorrectAlpha(curves.semiRotCurve, curves.semiLocCurve, correction);
                    SetupTransition(_startValRot, _startValLoc, curves.semiRotCurve, curves.semiLocCurve);
                }

                _pushTarget = Profile.pushAmount;

                _lastFrameTime = correction;
                _isLooping = true;
            };

            autoState.onStop = () =>
            {
                if (!_isLooping) return;
                float tempRot = _tempRotCurve.GetCurveLength();
                float tempLoc = _tempLocCurve.GetCurveLength();
                _lastFrameTime = tempRot > tempLoc ? tempRot : tempLoc;
                _isPlaying = true;
            };

            _stateMachine.Add(semiState);
            _stateMachine.Add(autoState);
        }

        private void SetupTransition(Vector3 startRot, Vector3 startLoc, VectorCurve rot, VectorCurve loc)
        {
            if (!rot.IsValid() || !loc.IsValid()) { Debug.Log("RecoilAnimation: Rot or Loc curve is nullptr"); return; }

            _startValRot = startRot; _startValLoc = startLoc;
            _canRestRot = _canRestLoc = new StartRest(true, true, true);

            _tempRotCurve = rot; _tempLocCurve = loc;
            _lastFrameTime = Mathf.Max(rot.GetCurveLength(), loc.GetCurveLength());
            PlayFromStart();
        }

        private void CorrectAlpha(VectorCurve rot, VectorCurve loc, float time)
        {
            Vector3 curveAlpha = rot.GetValue(time);
            _startValRot.x = Mathf.LerpUnclamped(_startValRot.x, _targetRot.x, curveAlpha.x);
            _startValRot.y = Mathf.LerpUnclamped(_startValRot.y, _targetRot.y, curveAlpha.y);
            _startValRot.z = Mathf.LerpUnclamped(_startValRot.z, _targetRot.z, curveAlpha.z);

            curveAlpha = loc.GetValue(time);
            _startValLoc.x = Mathf.LerpUnclamped(_startValLoc.x, _targetLoc.x, curveAlpha.x);
            _startValLoc.y = Mathf.LerpUnclamped(_startValLoc.y, _targetLoc.y, curveAlpha.y);
            _startValLoc.z = Mathf.LerpUnclamped(_startValLoc.z, _targetLoc.z, curveAlpha.z);
        }

        private void PlayFromStart() { _playBack = 0f; _isPlaying = true; }
        private float GetDelta() => Time.unscaledTime - _lastTimeShot;
    }
}
