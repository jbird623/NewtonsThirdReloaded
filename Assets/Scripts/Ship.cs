﻿using System.Collections;
using System.Collections.Generic;
using System;
using System.Linq;
using UnityEngine;
using JBirdEngine;

public enum ShipModifiers {
    Snake = 1,
    BustedEngine = 2,
    Squad = 4,
}

public enum ShipAbility {
    None = 0,
    RamThrusters = 1,
    CloakingDevice = 2,
    VendingMachine = 3,
    ChargeShot = 4,
}

public enum Layers {
    PlayerShip = 8,
    PlayerBullet = 9,
    EnemyShip = 10,
    EnemyBullet = 11,
    NeutralObject = 12,
}

[System.Serializable]
public class PartAnchor {
    public Vector3 relativePos;
    public Vector3 relativeDir;
}

public static class AnchorExtensions {
    public static void Orient (this MonoBehaviour mb, PartAnchor anchor, bool mirror) {
        mb.transform.localPosition = mirror ? anchor.relativePos.NegateX() : anchor.relativePos;
        mb.transform.localRotation = Quaternion.Euler(mirror ? anchor.relativeDir.NegateY() : anchor.relativeDir);
    }
}

public class Ship : MonoBehaviour, IShootable, ICollidable {

    [Header("Ship Stats")]
    public float mass;
    public float maxHealth;
    public float maxArmor;
    public float projectileDamageReduction;
    public float lowHealthActual;
    public float lowHealthVisual;

    [Header("Ship Mods")]
    [EnumHelper.EnumFlags]
    public ShipModifiers mods;
    public float brokenEngineRecoilModifier;
    public float brokenEngineDamageModifier;
    public float brokenEngineRamModifier;
    public ShipAbility ability;
    public float ramCooldown;

    [Header("Parts")]
    public Engine engine;
    public Weapon weapon;
    public Shield shield;

    [Header("Part Mods")]
    [EnumHelper.EnumFlags]
    public WeaponModifiers weaponMods;
    public float sideMountDamageModifier;
    public float spreadShotDamageModifier;
    [EnumHelper.EnumFlags]
    public ShieldModifiers shieldMods;

    [Header("RCS Thrusters")]
    public Engine rightSideThruster;
    public Engine leftSideThruster;
    public Engine frontRightThruster;
    public Engine frontLeftThruster;
    public Engine backRightThruster;
    public Engine backLeftThruster;

    [Header("Anchor Points")]
    public PartAnchor mainWeaponAnchor;
    public PartAnchor spreadWeaponAnchor;
    public PartAnchor sideWeaponAnchor;
    public PartAnchor engineAnchor;
    public PartAnchor shieldAnchor;

    [Header("Easing")]
    [Range(0.01f, 1f)]
    public float yawEasing;
    [Range(0.01f, 1f)]
    public float friction;

    [Header("Runtime")]
    public float health;
    public float armor;
    public float maxSpeedIncrease;
    public Vector2 targetYaw;
    public Vector3 thrustVelocity;
    public bool mainThrusterActive;
    public bool mainWeaponActive;
    public bool driftActive;
    public Vector3 lastThrustDirection;
    public float abilityCooldown;

    [Header("Physics")]
    public Layers shipLayer;
    public Layers bulletLayer;

    [Header("Collisions")]
    public Collider collider;
    public float damageModifier;
    public float damageReductionModifier;
    public float elasticity;

    [Header("Particles")]
    public ParticleSystem healthParticles;
    public ParticleSystem armorParticles;
    public ParticleSystem coolantParticles;

    List<Weapon> weapons;

    float currentMaxSpeed {
        get {
            return engine.maxSpeed + maxSpeedIncrease;
        }
    }

    float currentDamageModifier {
        get {
            return 1f
                * (HasMod(WeaponModifiers.SideMounts) ? sideMountDamageModifier : 1f)
                * (HasMod(WeaponModifiers.SpreadNozzle) ? spreadShotDamageModifier : 1f)
                * (HasMod(ShipModifiers.BustedEngine) ? brokenEngineDamageModifier : 1f);
        }
    }

    void Start () {
        if (collider == null) {
            collider = GetComponent<Collider>();
        }
        SetWeaponPositions();
        SetShipLayer();
    }

    void SetWeaponPositions () {
        weapons = new List<Weapon>();
        weapons.Add(weapon);
        if (HasMod(WeaponModifiers.SideMounts)) {
            Weapon rightMount = Instantiate(weapon, transform);
            rightMount.Orient(sideWeaponAnchor, false);
            Weapon leftMount = Instantiate(weapon, transform);
            leftMount.Orient(sideWeaponAnchor, true);
            weapons.Add(rightMount);
            weapons.Add(leftMount);
        }
        if (HasMod(WeaponModifiers.SpreadNozzle)) {
            Weapon rightSpread = Instantiate(weapon, transform);
            rightSpread.Orient(spreadWeaponAnchor, false);
            Weapon leftSpread = Instantiate(weapon, transform);
            leftSpread.Orient(spreadWeaponAnchor, true);
            weapons.Add(rightSpread);
            weapons.Add(leftSpread);
        }

    }

    void SetShipLayer () {
        gameObject.layer = Convert.ToInt32(shipLayer);
        foreach (Transform t in GetComponentsInChildren<Transform>()) {
            t.gameObject.layer = Convert.ToInt32(shipLayer);
        }
    }

    public bool HasMod (ShipModifiers mod) {
        return EnumHelper.ContainsFlag(mods, mod);
    }

    public bool HasMod (WeaponModifiers mod) {
        return EnumHelper.ContainsFlag(weaponMods, mod);
    }

    public bool HasMod (ShieldModifiers mod) {
        return EnumHelper.ContainsFlag(shieldMods, mod);
    }

    void FixedUpdate () {
        // Do this first so we can use abilities as soon as they're ready
        AbilityCooldown();
        // Turn first, for accuracy
        MoveTowardsTargetYaw();
        // Fire weapons
        FireWeaponAndHandleKickback();
        // Calculate Thrust
        AdjustMaxSpeed();
        CalculateTotalThrust();
        AdjustThrustFromBraking();
        ClampVelocity();
        MoveFromThrust();
        // Overheat Damage
        CalculateOverheatDamage();
    }

    void AbilityCooldown () {
        abilityCooldown = Mathf.Clamp(abilityCooldown - Time.fixedDeltaTime, 0f, abilityCooldown);
    }

    public void Interact (Projectile proj) {
        proj.Contact(this);
        TakeRecoil(proj.velocity * (proj.mass / mass));
        float dmg = CalculateProjectileDamageReduction(proj.damage);
        TakeDamage(dmg, true, proj.transform.position);
    }

    void TakeRecoil (Vector3 recoil) {
        thrustVelocity += recoil;
        if (thrustVelocity.magnitude > currentMaxSpeed) {
            thrustVelocity = thrustVelocity.normalized * currentMaxSpeed;
        }
    }

    public void SetTargetYaw (Vector2 yaw) {
        targetYaw = yaw.normalized;
    }

    public void MoveTowardsTargetYaw () {
        Vector3 target = new Vector3(targetYaw.x, 0, targetYaw.y).normalized;
        float azimuth = transform.forward.GetAzimuth(Vector3.zero, Vector3.up, target);
        if (azimuth > 180f) {
            azimuth -= 360f;
        }
        float easeAngle = azimuth * yawEasing;
        Vector3 easeTarget = VectorHelper.FromAzimuthAndElevation(easeAngle, 0f, Vector3.up, transform.forward).normalized;
        if (easeTarget.magnitude != 0f) {
            transform.rotation = Quaternion.LookRotation(VectorHelper.FromAzimuthAndElevation(easeAngle, 0f, Vector3.up, transform.forward), Vector3.up);
        }
    }

    public void FireWeaponAndHandleKickback () {
        if (mainWeaponActive) {
            Vector3 kickback = weapons.Aggregate(Vector3.zero, (v, w) => v + w.TryFire(mass, bulletLayer, currentDamageModifier)); // update this line for spread/side shot
            if (HasMod(ShipModifiers.BustedEngine)) {
                kickback *= brokenEngineRecoilModifier;
            }
            else {
                Vector3 mitigation = engine.GetKickbackMitigation(mass);
                if (mitigation.magnitude > kickback.magnitude) {
                    engine.OverheatFromThrust(-kickback);
                    kickback = Vector3.zero;
                }
                else if (mitigation.magnitude > 0f) {
                    engine.OverheatFromThrust(mitigation);
                    kickback += mitigation;
                }
            }
            thrustVelocity += kickback;
        }
    }

    public void CalculateTotalThrust () {
        if (mainThrusterActive && !HasMod(ShipModifiers.BustedEngine)) {
            Vector3 deltaThrust = engine.GetDeltaThrust(mass);
            if (deltaThrust.magnitude > 0f) {
                engine.TurnOnParticles();
            }
            else {
                engine.TurnOffParticles();
            }
            thrustVelocity += deltaThrust;
            engine.OverheatFromThrust(deltaThrust);
            lastThrustDirection = transform.forward;
        }
    }

    void AdjustMaxSpeed () {
        maxSpeedIncrease = Mathf.Clamp(maxSpeedIncrease - engine.ramSpeedFalloff * Time.fixedDeltaTime, 0f, maxSpeedIncrease);
    }

    public void ClampVelocity () {
        float forwardComponent = Vector3.Dot(thrustVelocity, lastThrustDirection);
        if (forwardComponent > currentMaxSpeed || forwardComponent < -currentMaxSpeed) {
            thrustVelocity = (thrustVelocity / thrustVelocity.magnitude) * currentMaxSpeed;
        }
    }

    public void AdjustThrustFromBraking () {
        // left rcs
        float leftThrustComponent = Vector3.Dot(thrustVelocity, -transform.right);
        if (driftActive && leftThrustComponent > 0.01f) {
            Vector3 leftThrust = leftSideThruster.GetDeltaThrust(mass);
            if (leftThrust.magnitude > leftThrustComponent) {
                leftThrust = leftThrust.normalized * leftThrustComponent;
            }
            thrustVelocity += leftThrust;
            leftSideThruster.TurnOnParticles();
        }
        else {
            leftSideThruster.TurnOffParticles();
        }
        // right rcs
        float rightThrustComponent = Vector3.Dot(thrustVelocity, transform.right);
        if (driftActive && rightThrustComponent > 0.01f) {
            Vector3 rightThrust = rightSideThruster.GetDeltaThrust(mass);
            if (rightThrust.magnitude > rightThrustComponent) {
                rightThrust = rightThrust.normalized * rightThrustComponent;
            }
            thrustVelocity += rightThrust;
            rightSideThruster.TurnOnParticles();
        }
        else {
            rightSideThruster.TurnOffParticles();
        }
        // forward rcs
        float frontThrustComponent = Vector3.Dot(thrustVelocity, transform.forward);
        if (driftActive && frontThrustComponent > 0.01f && !mainThrusterActive) {
            Vector3 frontThrust = frontLeftThruster.GetDeltaThrust(mass) + frontRightThruster.GetDeltaThrust(mass);
            if (frontThrust.magnitude > frontThrustComponent) {
                frontThrust = frontThrust.normalized * frontThrustComponent;
            }
            thrustVelocity += frontThrust;
            frontLeftThruster.TurnOnParticles();
            frontRightThruster.TurnOnParticles();
        }
        else {
            frontLeftThruster.TurnOffParticles();
            frontRightThruster.TurnOffParticles();
        }
        // backward rcs
        float backThrustComponent = Vector3.Dot(thrustVelocity, -transform.forward);
        if (driftActive && backThrustComponent > 0.01f && !mainThrusterActive) {
            Vector3 backThrust = backLeftThruster.GetDeltaThrust(mass) + backRightThruster.GetDeltaThrust(mass);
            if (backThrust.magnitude > backThrustComponent) {
                backThrust = backThrust.normalized * backThrustComponent;
            }
            thrustVelocity += backThrust;
            backLeftThruster.TurnOnParticles();
            backRightThruster.TurnOnParticles();
        }
        else {
            backLeftThruster.TurnOffParticles();
            backRightThruster.TurnOffParticles();
        }
    }

    public void MoveFromThrust () {
        transform.position += thrustVelocity * Time.fixedDeltaTime;
    }

    public void CalculateOverheatDamage () {
        if (engine.IsOverheating()) {
            TakeDamage(engine.GetOverheatDamage());
        }
    }

    public virtual void TakeDamage (float dmg, bool fromProjectile, Vector3 dmgPos) {
        if (fromProjectile || Mathf.RoundToInt(dmg) > 0) {
            DamageNumberController.instance.SpawnDamageNumber(dmg, fromProjectile ? projectileDamageReduction : damageReductionModifier, dmgPos, shipLayer == Layers.PlayerShip);
        }
        TakeDamage(dmg);
    }

    public void TakeDamage (float dmg) {
        float healthDmg = Mathf.Clamp(dmg - armor, 0f, dmg);
        armor = Mathf.Clamp(armor - dmg, 0f, armor);
        health = Mathf.Clamp(health - healthDmg, 0f, maxHealth);
    }

    public void RepairDamage (float dmg) {
        if (health <= lowHealthActual) {
            if (dmg >= 1f) {
                float visualHealth = (health / lowHealthActual) * lowHealthVisual;
                dmg += lowHealthVisual - visualHealth;
            }
            else {
                dmg = dmg * 2f;
            }
        }
        if (dmg >= 1f) {
            healthParticles.Play();
        }
        health = Mathf.Clamp(health + dmg, 0f, maxHealth);
    }

    public void AddArmor (float extra) {
        armorParticles.Play();
        armor = Mathf.Clamp(armor + extra, 0f, maxArmor);
    }

    public void ActivateThruster () {
        mainThrusterActive = true;
    }

    public void DeactivateThruster () {
        engine.TurnOffParticles();
        mainThrusterActive = false;
    }

    public void ActivateDrift () {
        driftActive = true;
    }

    public void DeactivateDrift () {
        driftActive = false;
    }

    public void ActivateWeapon () {
        mainWeaponActive = true;
    }

    public void DeactivateWeapon () {
        mainWeaponActive = false;
    }

    public void ActivateAbility () {
        switch (ability) {
            case ShipAbility.RamThrusters:
                if (abilityCooldown == 0f) {
                    RamBoost();
                }
                break;
        }
    }

    public void DeactivateAbility () {

    }

    public float GetAbilityCooldown () {
        switch (ability) {
            case ShipAbility.RamThrusters:
                return ramCooldown;
        }
        return 0f;
    }

    public void RamBoost () {
        maxSpeedIncrease = engine.maxSpeed * engine.ramMaxSpeedMultiplier;
        Vector3 newThrust = transform.forward * currentMaxSpeed * (HasMod(ShipModifiers.BustedEngine) ? brokenEngineRamModifier : 1f);
        engine.OverheatFromThrust(newThrust - thrustVelocity);
        if (HasMod(ShipModifiers.BustedEngine)) {
            if (Vector3.Dot(thrustVelocity, newThrust) > 0f) {
                thrustVelocity += newThrust;
            }
            else {
                thrustVelocity = newThrust;
            }
        }
        else {
            thrustVelocity = newThrust;
        }
        lastThrustDirection = transform.forward;
        abilityCooldown = ramCooldown;
        engine.PlayParticleBurst();
    }

    void OnTriggerEnter (Collider other) {
        ICollidable collidable = other.GetComponent<ICollidable>();
        if (collidable != null) {
            this.HandleCollision(collidable);
        }
    }

    void OnTriggerStay (Collider other) {
        ICollidable collidable = other.GetComponent<ICollidable>();
        if (collidable != null) {
            this.HandleCollision(collidable);
        }
    }

    public float GetMass () {
        return mass;
    }

    public Vector3 GetVelocity () {
        return thrustVelocity;
    }

    public void SetVelocity (Vector3 vel) {
        thrustVelocity = vel;
    }

    public Vector3 GetPosition () {
        return transform.position;
    }

    public float GetElasticity () {
        return elasticity;
    }

    public float GetDamage (float momentumDiff) {
        return momentumDiff * damageModifier;
    }

    public float CalculateCollisionDamageReduction (float dmg) {
        return Mathf.Clamp(dmg - dmg * damageReductionModifier, 0f, Mathf.Infinity);
    }

    public float CalculateProjectileDamageReduction (float dmg) {
        return Mathf.Clamp(dmg - dmg * projectileDamageReduction, 0f, Mathf.Infinity);
    }

}
