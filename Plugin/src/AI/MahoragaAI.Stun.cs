using GameNetcodeStuff;
using MahoragaLC.Config;
using MahoragaLC.Patches;
using Unity.Netcode;
using UnityEngine;

namespace MahoragaLC.AI
{
    public partial class MahoragaAI
    {
        private readonly TimedStunExposure _timedStuns = new TimedStunExposure();
        private int _lastFlashInstanceId;
        private float _lastTimedStunApplication = float.NegativeInfinity;

        public override void SetEnemyStunned(bool setToStunned, float setToStunTime = 1f,
            PlayerControllerB? setStunnedByPlayer = null)
        {
            if (isEnemyDead || IsSummonInvulnerable)
            {
                return;
            }

            if (!setToStunned)
            {
                base.SetEnemyStunned(false, setToStunTime, setStunnedByPlayer);
                return;
            }

            bool isAdapting = currentBehaviourStateIndex == (int)MahoragaState.Adapting;
            StunGrenadeItem? grenade = ExplosionTrackingPatch.FlashGrenade;
            var held = setStunnedByPlayer?.currentlyHeldObjectServer;
            bool isFlash = grenade != null || ExplosionTrackingPatch.InFlash;
            bool isZapGun = !isFlash && held is PatcherTool;
            if (!isZapGun)
            {
                if (IsServer && setToStunTime > 0f && !isAdapting && enemyType.canBeStunned)
                {
                    ApplyTimedStunServer(setToStunTime, setStunnedByPlayer, grenade, held, isFlash);
                }
                return;
            }

            if (isAdapting || _adaptation.IsAdapted(AdaptationCategory.ZapGun))
            {
                return;
            }

            float duration = setToStunTime;
            if (IsServer)
            {
                MahoragaConfig? cfg = MahoragaConfig.Current;
                StunEvaluation eval = _adaptation.EvaluateStun(
                    AdaptationCategory.ZapGun, enemyHP, cfg?.MaxHP.Value ?? 18, false,
                    cfg?.Adaptation ?? _fallbackAdaptation, Time.time);
                if (setStunnedByPlayer != null && PlayerIsTargetable(setStunnedByPlayer))
                {
                    RegisterPlayerAggression(setStunnedByPlayer, 2, Time.time);
                }

                if (eval.TriggeredAdaptation)
                {
                    EnterAdaptationStateServer(AdaptationCategory.ZapGun, eval.FinalHP);
                    return;
                }
                if (eval.TriggeredWheelClick)
                {
                    TriggerWheelClickClientRpc((byte)AdaptationCategory.ZapGun, (byte)eval.StageAfter,
                        _adaptation.AdaptedMask, _adaptation.WheelStepCount, enemyHP, playSpinAnim: false);
                }
                duration *= eval.StunTimeMultiplier;
            }
            base.SetEnemyStunned(true, duration, setStunnedByPlayer);
        }

        private void ApplyTimedStunServer(float duration, PlayerControllerB? player, StunGrenadeItem? grenade,
            GrabbableObject? held, bool isFlash)
        {
            if (grenade != null)
            {
                int id = grenade.GetInstanceID();
                if (_lastFlashInstanceId == id)
                {
                    return;
                }
                _lastFlashInstanceId = id;
            }
            else if (Time.time - _lastTimedStunApplication <= 0.12f)
            {
                return;
            }

            _lastTimedStunApplication = Time.time;
            var source = grenade?.GetType() ?? (isFlash ? typeof(StunGrenadeItem) : held?.GetType()) ?? typeof(TimedStunExposure);
            float multiplier = _timedStuns.Register(source, out bool wheelTurns);
            if (wheelTurns)
            {
                AdaptationCategory visualCategory = isFlash ? AdaptationCategory.StunGrenade : AdaptationCategory.EquipmentStun;
                TriggerWheelClickClientRpc((byte)visualCategory, 1, _adaptation.AdaptedMask,
                    _adaptation.AdvanceWheelStep(), enemyHP, playSpinAnim: false);
            }

            if (player != null && PlayerIsTargetable(player))
            {
                RegisterPlayerAggression(player, 2, Time.time);
            }

            // Keep the game-provided duration units. A sustained beam, if active, still owns its separate timer.
            stunNormalizedTimer = Mathf.Max(stunNormalizedTimer, duration * multiplier);
            stunnedByPlayer = player;
            if (agent != null && agent.enabled)
            {
                agent.speed = 0f;
            }
            if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                SyncTimedStunClientRpc(stunNormalizedTimer);
            }
        }

        [ClientRpc]
        private void SyncTimedStunClientRpc(float remaining, ClientRpcParams clients = default)
        {
            if (!IsServer && !isEnemyDead)
            {
                stunNormalizedTimer = Mathf.Max(stunNormalizedTimer, remaining);
            }
        }
    }
}
