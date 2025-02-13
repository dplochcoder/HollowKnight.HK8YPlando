﻿using HK8YPlando.IC;
using HK8YPlando.Scripts.InternalLib;
using HK8YPlando.Scripts.Proxy;
using HK8YPlando.Scripts.SharedLib;
using HK8YPlando.Util;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using ItemChanger.Extensions;
using ItemChanger.FsmStateActions;
using ItemChanger.Internal.Preloaders;
using SFCore.Utils;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HK8YPlando.Scripts.Platforming;

[Shim]
internal class TraitorLords : MonoBehaviour
{
    [ShimField] public HeroDetectorProxy? Trigger;

    [ShimField] public float StartDelay;
    [ShimField] public GameObject? Spawn1;
    [ShimField] public bool Spawn1FacingRight;
    [ShimField] public float SpawnDelay;
    [ShimField] public GameObject? Spawn2;
    [ShimField] public bool Spawn2FacingRight;
    [ShimField] public float LeftX;
    [ShimField] public float RightX;
    [ShimField] public float MainY;
    [ShimField] public float AttackCooldown;
    [ShimField] public float SickleCooldown;
    [ShimField] public float SlamCooldown;

    private void Awake() => this.StartLibCoroutine(Run());

    private PlayMakerFSM? traitor1;
    private bool traitor1Dead;
    private PlayMakerFSM? traitor2;
    private bool traitor2Dead;

    private IEnumerator<CoroutineElement> Run()
    {
        yield return Coroutines.SleepFrames(10);
        Destroy(GameObject.Find("/_Transition Gates/top1")!);

        yield return Coroutines.SleepUntil(() => Trigger!.Detected());

        var mod = BrettasHouse.Get();
        if (mod.CheckpointPriority < 5)
        {
            mod.CheckpointScene = gameObject.scene.name;
            mod.CheckpointGate = "top1";
            mod.CheckpointPriority = 5;
        }

        yield return Coroutines.SleepSeconds(StartDelay);

        traitor1 = SpawnTraitorLord(Spawn1!.transform.position, Spawn1FacingRight, () =>
        {
            traitor1Dead = true;
            if (!traitor2Dead && traitor2 != null) RageMode(traitor2);
        });

        yield return Coroutines.SleepSeconds(SpawnDelay);
        traitor2 = SpawnTraitorLord(Spawn2!.transform.position, Spawn2FacingRight, () =>
        {
            traitor2Dead = true;
            if (!traitor1Dead) RageMode(traitor1);
        });

        // Debug
        // this.StartLibCoroutine(DelayedKill(traitor2.gameObject));

        yield return Coroutines.SleepUntil(() => traitor1Dead && traitor2Dead);

        // FIXME: Door
    }

    private IEnumerator<CoroutineElement> DelayedKill(GameObject victim)
    {
        yield return Coroutines.SleepSeconds(5);
        victim.GetComponent<HealthManager>().ApplyExtraDamage(9999);
    }

    // Force slams to not be overlapping.
    private PlayMakerFSM? lastSlammer;
    private float slamCooldown;

    private void Update()
    {
        if (attackCooldown > 0)
        {
            attackCooldown -= Time.deltaTime;
            if (attackCooldown < 0) attackCooldown = 0;
        }
        if (sickleCooldown > 0)
        {
            sickleCooldown -= Time.deltaTime;
            if (sickleCooldown < 0) sickleCooldown = 0;
        }
        if (slamCooldown > 0)
        {
            slamCooldown -= Time.deltaTime;
            if (slamCooldown < 0) slamCooldown = 0;
        }
    }

    // Force attacks to be at least slightly staggered.
    private PlayMakerFSM? lastAttacker;
    private float attackCooldown;

    // Force sickle throws to be staggered.
    private PlayMakerFSM? lastSickler;
    private float sickleCooldown;

    private PlayMakerFSM SpawnTraitorLord(Vector3 position, bool facingRight, Action onDeath)
    {
        var prefab = HK8YPlandoPreloader.Instance.TraitorLord;
        var pos = position;
        pos.z = prefab.transform.position.z;
        var obj = Instantiate(prefab, pos, Quaternion.identity);

        if (!facingRight) obj.transform.localScale = new(-1, 1, 1);
        var mid = (LeftX + RightX) / 2;

        var fsm = obj.LocateMyFSM("Mantis");
        fsm.FsmVariables.GetFsmGameObject("Self").Value = obj;

        List<string> sensors = ["Back Range", "Front Range", "Walk Range"];
        foreach (var sensor in sensors) GameObjectExtensions.FindChild(obj, sensor).SetActive(false);
        obj.AddComponent<TraitorLordExpandedVision>();

        fsm.GetFsmState("Check L").GetFirstActionOfType<FloatCompare>().float2.Value = mid + 1.5f;
        fsm.GetFsmState("Check R").GetFirstActionOfType<FloatCompare>().float2.Value = mid - 1.5f;
        fsm.GetFsmState("DSlash").GetFirstActionOfType<FloatCompare>().float2.Value = MainY;
        fsm.GetFsmState("Fall").GetFirstActionOfType<FloatCompare>().float2.Value = MainY;
        fsm.GetFsmState("Intro Land").GetFirstActionOfType<SetPosition>().y.Value = MainY;
        fsm.GetFsmState("Land").GetFirstActionOfType<SetPosition>().y.Value = MainY;
        fsm.GetFsmState("Roar").GetFirstActionOfType<SetFsmString>().setValue = "BRETTOR_LORD2";

        fsm.GetFsmState("Sickle Antic").AddFirstAction(new Lambda(() =>
        {
            if (sickleCooldown > 0 && lastSickler != null && lastSickler != this)
            {
                fsm.SetState("Cooldown");
                return;
            }

            lastSickler = fsm;
            sickleCooldown = SickleCooldown;
        }));

        fsm.GetFsmState("Too Close?").AddFirstAction(new Lambda(() =>
        {
            if (slamCooldown > 0 && lastSlammer != null && lastSlammer != this)
            {
                fsm.SetState("Idle");
                return;
            }

            lastSlammer = fsm;
            slamCooldown = SlamCooldown;
        }));

        List<string> attackStates = ["Feint?", "Jump Antic"];
        foreach (var attackState in attackStates)
        {
            fsm.GetFsmState(attackState).AddFirstAction(new Lambda(() =>
            {
                if (attackCooldown > 0 && lastAttacker != null && lastAttacker != this)
                {
                    fsm.SetState("Cooldown");
                    return;
                }

                lastAttacker = fsm;
                attackCooldown = AttackCooldown;
            }));
        }

        obj.SetActive(true);
        fsm.SetState("Fall");

        // Shorten death anim.
        this.StartLibCoroutine(ModifyCorpseFsm(fsm));

        // Reduce health.
        var health = fsm.gameObject.GetComponent<HealthManager>();
        health.hp = 700;
        fsm.gameObject.GetComponent<HealthManager>().OnDeath += () => onDeath();
        fsm.GetFsmState("Slam?").GetFirstActionOfType<IntCompare>().integer2 = 400;
        return fsm;
    }

    private IEnumerator<CoroutineElement> ModifyCorpseFsm(PlayMakerFSM fsm)
    {
        while (true)
        {
            var blow = GameObjectExtensions.FindChild(fsm.gameObject, "Corpse Traitor Lord(Clone)")?.LocateMyFSM("FSM");
            if (blow == null)
            {
                yield return Coroutines.SleepFrames(1);
                continue;
            }

            blow.GetFsmState("Init").AddFirstAction(new Lambda(() =>
            {
                if (enragedFsm == fsm) return;

                blow.GetFsmState("Init").GetFirstActionOfType<Wait>().time = 0.15f;
                blow.GetFsmState("Steam").GetFirstActionOfType<Wait>().time = 0.45f;
                blow.GetFsmState("Ready").GetFirstActionOfType<Wait>().time = 0.2f;
            }));
            break;
        }
    }

    [ShimField] public int RageHPBoost;

    [ShimField] public float RageRoarTime;
    [ShimField] public float RageRoarSpeedup;

    [ShimField] public float RageAttackSpeed;
    [ShimField] public float RageSickleSpeed;
    [ShimField] public float RageWalkSpeed;
    [ShimField] public float RageWaveSpeed;

    [ShimField] public float RageAttack1Speedup;
    [ShimField] public float RageAttackAnticSpeedup;
    [ShimField] public float RageAttackRecoverSpeedup;
    [ShimField] public float RageAttackSwipeSpeedup;
    [ShimField] public float RageCooldownSpeedup;
    [ShimField] public float RageDSlashSpeedup;
    [ShimField] public float RageDSlashAnticSpeedup;
    [ShimField] public float RageFeintSpeedup;
    [ShimField] public float RageFeint2Speedup;
    [ShimField] public float RageJumpSpeedup;
    [ShimField] public float RageJumpAnticSpeedup;
    [ShimField] public float RageLandSpeedup;
    [ShimField] public float RageSickleAnticSpeedup;
    [ShimField] public float RageSickleThrowSpeedup;
    [ShimField] public float RageSickleThrowCooldownSpeedup;
    [ShimField] public float RageSickleThrowRecoverSpeedup;
    [ShimField] public float RageSlamAnticSpeedup;
    [ShimField] public float RageSlammingSpeedup;
    [ShimField] public float RageSlamEndSpeedup;
    [ShimField] public float RageTurnSpeedup;
    [ShimField] public float RageWalkSpeedup;
    [ShimField] public float RageWavesSpeedup;

    private PlayMakerFSM? enragedFsm;

    private void RageMode(PlayMakerFSM fsm)
    {
        enragedFsm = fsm;
        var accel = GameObjectExtensions.GetOrAddComponent<AnimationAccelerator>(fsm.gameObject);

        fsm.gameObject.GetComponent<HealthManager>().hp += RageHPBoost;

        var roar = fsm.GetFsmState("Roar");
        var audio = roar.GetFirstActionOfType<AudioPlayerOneShot>();
        audio.pitchMin.Value = 1.25f;
        audio.pitchMax.Value = 1.25f;
        audio.audioClips[0] = HK8YPlandoPreloader.Instance.OblobbleRoar;

        roar.RemoveActionsOfType<SendEventByName>();
        roar.GetFirstActionOfType<Wait>().time.Value = RageRoarTime;
        roar.RemoveActionsOfType<ActivateGameObject>();

        fsm.GetFsmState("Roar End").AccelerateAnimation(accel, RageRoarSpeedup);
        fsm.GetFsmState("Roar Recover").AccelerateAnimation(accel, RageRoarSpeedup);

        Wrapped<bool> raged = new(false);
        List<string> stateNames = ["Attack Antic", "Attack Choice", "Cooldown", "Idle", "Sick Throw CD", "Slam Antic", "Walk"];
        foreach (var name in stateNames)
        {
            var state = fsm.GetFsmState(name);
            state.AddFirstAction(new Lambda(() =>
            {
                if (!raged.Value)
                {
                    raged.Value = true;
                    fsm.SetState("Roar");

                    this.StartLibCoroutine(DelayedRoarAnim(fsm));
                    ActualRageMode(fsm);
                }
            }));
        }
    }

    private IEnumerator<CoroutineElement> DelayedRoarAnim(PlayMakerFSM fsm)
    {
        yield return Coroutines.SleepFrames(1);
        fsm.GetComponent<tk2dSpriteAnimator>().Play("Roar");
    }

    private void ActualRageMode(PlayMakerFSM fsm)
    {
        var accel = GameObjectExtensions.GetOrAddComponent<AnimationAccelerator>(fsm.gameObject);

        fsm.FsmVariables.GetFsmFloat("Attack Speed").Value = RageAttackSpeed;
        fsm.FsmVariables.GetFsmFloat("DSlash Speed").Value = RageAttackSpeed;
        fsm.FsmVariables.GetFsmFloat("Sickle Speed Base").Value = RageSickleSpeed;

        fsm.GetFsmState("Attack 1").AccelerateAnimation(accel, RageAttack1Speedup);
        fsm.GetFsmState("Attack Antic").AccelerateAnimation(accel, RageAttackAnticSpeedup);
        fsm.GetFsmState("Attack Recover").AccelerateAnimation(accel, RageAttackRecoverSpeedup);
        fsm.GetFsmState("Attack Swipe").AccelerateAnimation(accel, RageAttackSwipeSpeedup);
        fsm.GetFsmState("DSlash").AccelerateAnimation(accel, RageDSlashSpeedup);
        fsm.GetFsmState("DSlash Antic").AccelerateAnimation(accel, RageDSlashAnticSpeedup);
        fsm.GetFsmState("Jump").AccelerateAnimation(accel, RageJumpSpeedup);
        fsm.GetFsmState("Jump Antic").AccelerateAnimation(accel, RageJumpAnticSpeedup);
        fsm.GetFsmState("Land").AccelerateAnimation(accel, RageLandSpeedup);
        fsm.GetFsmState("Sickle Antic").AccelerateAnimation(accel, RageSickleAnticSpeedup);
        fsm.GetFsmState("Sickle Throw Recover").AccelerateAnimation(accel, RageSickleThrowRecoverSpeedup);
        fsm.GetFsmState("Slam Antic").AccelerateAnimation(accel, RageSlamAnticSpeedup);
        fsm.GetFsmState("Turn").AccelerateAnimation(accel, RageTurnSpeedup);

        var cooldown = fsm.GetFsmState("Cooldown");
        cooldown.GetFirstActionOfType<Wait>().time = 0.25f / RageCooldownSpeedup;
        cooldown.AccelerateAnimation(accel, RageCooldownSpeedup);

        var feint = fsm.GetFsmState("Feint");
        feint.GetFirstActionOfType<Wait>().time = 0.2f / RageFeintSpeedup;
        feint.AccelerateAnimation(accel, RageFeintSpeedup);

        var feint2 = fsm.GetFsmState("Feint 2");
        feint2.GetFirstActionOfType<Wait>().time = 0.25f / RageFeint2Speedup;
        feint2.AccelerateAnimation(accel, RageFeint2Speedup);

        var sickleThrow = fsm.GetFsmState("Sickle Throw");
        sickleThrow.AccelerateAnimation(accel, RageSickleThrowSpeedup);
        sickleThrow.InsertBefore<PlayParticleEmitter>(new Lambda(() =>
        {
            var spawner = sickleThrow.GetFirstActionOfType<SpawnObjectFromGlobalPool>();
            var spawned = spawner.gameObject.Value.Spawn(spawner.spawnPoint.Value.transform.position, Quaternion.identity);
            spawned.GetComponent<Rigidbody2D>().velocity = new(fsm.FsmVariables.GetFsmFloat("Sickle Speed Base").Value * 3.5f * fsm.gameObject.transform.localScale.x, 0);
        }));

        var sickleThrowCooldown = fsm.GetFsmState("Sick Throw CD");
        sickleThrowCooldown.GetFirstActionOfType<Wait>().time = RageSickleThrowCooldownSpeedup / RageSickleThrowCooldownSpeedup;
        sickleThrowCooldown.AccelerateAnimation(accel, RageSlammingSpeedup);

        var slamming = fsm.GetFsmState("Slamming");
        slamming.GetFirstActionOfType<Wait>().time = 0.3f / RageSlammingSpeedup;
        slamming.AccelerateAnimation(accel, RageSlammingSpeedup);

        var slamEnd = fsm.GetFsmState("Slam End");
        slamEnd.GetFirstActionOfType<Wait>().time = 1.2f / RageSlamEndSpeedup;
        slamEnd.AccelerateAnimation(accel, RageSlamEndSpeedup);

        var walk = fsm.GetFsmState("Walk");
        walk.GetFirstActionOfType<ChaseObjectGround>().speedMax.Value = RageWalkSpeed;
        walk.AccelerateAnimation(accel, RageWalkSpeedup);

        var waves = fsm.GetFsmState("Waves");
        waves.GetFirstActionOfType<Wait>().time = 1f / RageWavesSpeedup;
        foreach (var action in waves.GetActionsOfType<SetVelocity2d>()) action.x.Value = Mathf.Sign(action.x.Value) * RageWaveSpeed;
    }
}

internal class TraitorLordExpandedVision : MonoBehaviour
{
    private GameObject? knight;
    private FsmBool? frontCheck;
    private FsmBool? backCheck;
    private FsmBool? walkCheck;

    private void Awake()
    {
        knight = HeroController.instance.gameObject;

        var fsm = gameObject.LocateMyFSM("Mantis");
        var vars = fsm.FsmVariables;
        frontCheck = vars.GetFsmBool("Front Range");
        backCheck = vars.GetFsmBool("Back Range");
        walkCheck = vars.GetFsmBool("Walk Range");
    }

    private const float TRAITOR_RANGE = 12;

    private void Update()
    {
        walkCheck!.Value = true;

        var x = gameObject.transform.position.x;
        var kx = knight!.transform.position.x;
        float dx = kx - x;

        if (gameObject.transform.localScale.x > 0)
        {
            frontCheck!.Value = dx >= 0 && dx <= TRAITOR_RANGE;
            backCheck!.Value = dx >= -TRAITOR_RANGE && dx <= 0;
        }
        else
        {
            frontCheck!.Value = dx >= -TRAITOR_RANGE && dx <= 0;
            backCheck!.Value = dx >= 0 && dx <= TRAITOR_RANGE;
        }
    }
}
