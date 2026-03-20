# CLAUDE.md

This file provides guidance to Claude Code when working with this Unity package.

## Unity Version

This package targets **Unity 6000.3**. Do not flag issues that are only relevant to older Unity versions.

## UI Component Design

UIDocument/UXML-backed MonoBehaviours in this package use a **fail-fast design by convention**: `ResolveElements()` throws `InvalidOperationException` on any missing element, and `Awake` throws `MissingReferenceException` for unset inspector refs. UI element fields are guaranteed valid after `OnEnable` or the component never reaches a running state. Do not flag missing null guards on UI element fields as issues.
