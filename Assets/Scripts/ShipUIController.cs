﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using JBirdEngine.ColorLibrary;

[System.Serializable]
public class UiBar {
    public enum Mode {
        PositiveX,
        PositiveY,
        NegativeX,
        NegativeY,
    }

    public Image progress;
    public Mode fillMode;
    public Color normalColor;
    public Color warningColor;
    public float flashTime;
    public float warningZoneMin;
    public float warningZoneMax;

    Coroutine flashRoutine;
    public float currentFlashTime;

    IEnumerator FlashWarningColor (float startFlashTime) {
        currentFlashTime = startFlashTime;
        float flashDirection = 1f;
        while (true) {
            currentFlashTime += (1f / flashTime) * Time.deltaTime * flashDirection;
            if (currentFlashTime >= 1f) {
                flashDirection = -1f;
                currentFlashTime = 1f;
            }
            if (currentFlashTime <= -1f) {
                flashDirection = 1f;
                currentFlashTime = -1f;
            }
            progress.color = ColorHelper.LerpHSV(normalColor, warningColor, currentFlashTime);
            yield return null;
        }
    }

    public void SetProgress (MonoBehaviour parent, float percent, float warningTime = 0f) {
        percent = Mathf.Clamp01(percent);
        switch (fillMode) {
            case Mode.PositiveX:
                progress.rectTransform.anchorMax = new Vector2(percent, 1f);
                progress.rectTransform.anchorMin = new Vector2(0f, 0f);
                break;
            case Mode.NegativeX:
                progress.rectTransform.anchorMax = new Vector2(1f, 1f);
                progress.rectTransform.anchorMin = new Vector2(1f - percent, 0f);
                break;
            case Mode.PositiveY:
                progress.rectTransform.anchorMax = new Vector2(1f, percent);
                progress.rectTransform.anchorMin = new Vector2(0f, 0f);
                break;
            case Mode.NegativeY:
                progress.rectTransform.anchorMax = new Vector2(1f, 1f);
                progress.rectTransform.anchorMin = new Vector2(0f, 1f - percent);
                break;
        }
        progress.rectTransform.sizeDelta = new Vector2(0f, 0f);
        if (percent <= warningZoneMax && percent >= warningZoneMin) {
            if (flashRoutine == null) {
                flashRoutine = parent.StartCoroutine(FlashWarningColor(warningTime));
            }
        }
        else if (flashRoutine != null) {
            parent.StopCoroutine(flashRoutine);
            flashRoutine = null;
            progress.color = normalColor;
        }
    }
}

public class ShipUIController : MonoBehaviour {

    public Ship target;
    public float healthDangerZone;

    public UiBar healthBar;
    public UiBar shieldBar;
    public UiBar overheatBar;
    public UiBar abilityBar;

    void Update () {
        overheatBar.SetProgress(this, target.engine.overheat / target.engine.overheatTime);
        if (target.engine.IsCausingOverheatDamage()) {
            healthBar.warningZoneMax = 1f;
        }
        else {
            healthBar.warningZoneMax = healthDangerZone;
        }
        healthBar.SetProgress(this, target.health / target.maxHealth, overheatBar.currentFlashTime);
        shieldBar.SetProgress(this, target.shield.health / target.shield.maxHealth);
        abilityBar.SetProgress(this, 1f - target.abilityCooldown / target.GetAbilityCooldown());
    }

}
