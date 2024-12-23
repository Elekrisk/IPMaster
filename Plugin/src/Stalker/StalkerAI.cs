
using System;
using System.Collections;
using GameNetcodeStuff;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements.Collections;

namespace IPMaster.Stalker
{
    class StalkerAI : EnemyAI
    {
        static int nextId = 0;
        int stalkerId = nextId++;

        bool registered = false;

        public int StalkerId { get { return stalkerId; } }

        float timeSinceDeath = 0.0f;
        float effectRamp = 0.0f;
        float stareTimer = 0.0f;

        bool destroyed = false;
        private float timeSinceLastAttack = 0.0f;

        enum State
        {
            Searching,
            JustSwitchedTarget,
            Following,
        }

        public override void Start()
        {
            base.Start();

            Plugin.Logger.LogInfo("Stalker Spawned");

            agent.speed = 0.75f;
            // transform.Find("model").Find("Cube").gameObject.layer = 0;

            StartSearch(transform.position);

            creatureAnimator.SetTrigger("startWalk");
        }

        public override void Update()
        {
            base.Update();
            AudioSource audioSource = transform.Find("NoiseSource").gameObject.GetComponent<AudioSource>();

            if (isEnemyDead)
            {
                if (!destroyed && timeSinceDeath >= 2.0f)
                {
                    destroyed = true;
                    base.KillEnemy(true);
                    return;
                }

                audioSource.volume = Mathf.Clamp01(Mathf.Pow(1.0f - (timeSinceDeath / 2.0f), 2));

                timeSinceDeath += Time.deltaTime;
            }

            if (!isEnemyDead && !registered)
            {
                effectRamp = 0.0f;
                audioSource.enabled = false;
                return;
            }
            audioSource.enabled = true;

            if (registered)
            {
                effectRamp = Mathf.MoveTowards(effectRamp, 1.0f, GetStareTimeIncreaseForDelta(Time.deltaTime));
                audioSource.volume = Mathf.Clamp01(Mathf.Pow(effectRamp, 2));

                var player = StartOfRound.Instance.localPlayerController.gameObject;
                var dist = Vector3.Distance(player.transform.position, gameObject.transform.position) + 10.0f * (1.0f - effectRamp);
                PostProcessorHandler.Instance.UpdateDistance(this, dist);
            }
        }

        public override void DoAIInterval()
        {
            base.DoAIInterval();
            if (isEnemyDead || StartOfRound.Instance.allPlayersDead)
            {
                return;
            }

            switch (currentBehaviourStateIndex)
            {
                case (int)State.Searching:
                    bool found = TargetClosestPlayer(1.5f, true);
                    if (!found && TargetClosestPlayer(1.5f, false))
                    {
                        if (Vector3.Distance(targetPlayer.transform.position, transform.position) > 10f)
                        {
                            targetPlayer = null;
                        }
                        else
                        {
                            found = true;
                        }
                    }
                    if (found)
                    {
                        Plugin.Logger.LogInfo($"Found player {targetPlayer!.playerUsername}");
                        StopSearch(currentSearch);
                        TargetPlayer(targetPlayer);
                        Plugin.Logger.LogInfo($"Starting to stare...");
                        SetDestinationToPosition(transform.position);
                        stareTimer = 0.0f;
                        SwitchToBehaviourState((int)State.JustSwitchedTarget);
                    }
                    break;
                case (int)State.JustSwitchedTarget:
                    // Stare at player, length dependeing on distance, ramping up effect
                    transform.LookAt(targetPlayer.transform);
                    if (stareTimer >= 1.0f)
                    {
                        Plugin.Logger.LogInfo($"Starting to move towards player...");
                        SetMovingTowardsTargetPlayer(targetPlayer);
                        SwitchToBehaviourState((int)State.Following);
                    }
                    stareTimer += GetStareTimeIncreaseForDelta(AIIntervalTime);
                    break;
                case (int)State.Following:
                    if (targetPlayer == null || targetPlayer.isPlayerDead || (Vector3.Distance(transform.position, targetPlayer.transform.position) > 20 && !CheckLineOfSightForPosition(targetPlayer.transform.position)))
                    {
                        Plugin.Logger.LogInfo("Lost player");
                        StartSearch(transform.position);
                        TargetPlayer(null);
                        SwitchToBehaviourState((int)State.Searching);
                    } else if (Vector3.Distance(targetPlayer.transform.position, transform.position) < 2f && timeSinceLastAttack >= 1.0f) {
                        timeSinceLastAttack = 0.0f;
                        DamagePlayerClientRpc(targetPlayer.playerClientId, 20);
                    }
                    break;
            }

            timeSinceLastAttack += AIIntervalTime;
        }

        float GetStareTimeIncreaseForDelta(float delta)
        {
            return delta * (1.0f / (11.0f - Mathf.Clamp(Vector3.Distance(targetPlayer.transform.position, transform.position), 1.0f, 10.0f)));
        }

        void TargetPlayer(PlayerControllerB? player)
        {
            if (IsServer)
            {
                TargetPlayerClientRpc(player?.actualClientId ?? ulong.MaxValue);
            }
            else if (IsClient)
            {
                TargetPlayerServerRpc(player?.actualClientId ?? ulong.MaxValue);
            }
        }


        [ServerRpc]
        void TargetPlayerServerRpc(ulong clientId)
        {
            TargetPlayerClientRpc(clientId);
        }

        [ClientRpc]
        void TargetPlayerClientRpc(ulong clientId)
        {
            PlayerControllerB? player = null;
            if (clientId != ulong.MaxValue)
            {
                int targetPlayerId = StartOfRound.Instance.ClientPlayerList.Get((ulong)clientId);
                player = StartOfRound.Instance.allPlayerScripts[targetPlayerId];
            }
            if (player == null && clientId != ulong.MaxValue)
            {
                Plugin.Logger.LogWarning($"Stalker could not switch to player with clientId : {clientId}");
                return;
            }

            targetPlayer = player;

            if (targetPlayer == StartOfRound.Instance.localPlayerController)
            {
                // We are target D:
                Register();
            }
            else
            {
                // We are not target :D
                Deregister();
            }
        }

        void Register()
        {
            if (!registered)
            {
                PostProcessorHandler.Instance.RegisterStalker(this);
                registered = true;
            }
        }

        void Deregister()
        {
            if (registered)
            {
                PostProcessorHandler.Instance.DeregisterStalker(this);
                registered = false;
            }
        }

        public override void HitEnemy(int force = 1, PlayerControllerB? playerWhoHit = null, bool playHitSFX = false, int hitID = -1)
        {
            base.HitEnemy(force, playerWhoHit, playHitSFX, hitID);
            if (isEnemyDead)
            {
                return;
            }
            enemyHP -= force;
            if (IsOwner)
            {
                if (enemyHP <= 0 && !isEnemyDead)
                {
                    StopCoroutine(searchCoroutine);
                    KillEnemyOnOwnerClient();
                    return;
                }

                if (playerWhoHit != null && playerWhoHit != targetPlayer)
                {
                    TargetPlayer(playerWhoHit);
                    SetMovingTowardsTargetPlayer(targetPlayer);
                    SwitchToBehaviourState((int)State.Following);
                }
            }
        }

        public override void KillEnemy(bool destroy = false)
        {
            PostProcessorHandler.Instance.DeregisterStalker(this);
            registered = false;

            base.KillEnemy();
        }

        [ClientRpc]
        void DamagePlayerClientRpc(ulong playerClientId, int amount) {
            if (GameNetworkManager.Instance.localPlayerController.playerClientId != playerClientId) {
                return;
            }

            Plugin.Logger.LogInfo("Damaged player");
            GameNetworkManager.Instance.localPlayerController.DamagePlayer(amount);
        }

        ~StalkerAI()
        {
            if (registered)
            {
                PostProcessorHandler.Instance.DeregisterStalker(this);
                registered = false;
            }
        }
    }
}
