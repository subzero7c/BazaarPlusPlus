import { mkdtempSync, mkdirSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { test, expect } from 'vitest';

import {
  assertMacosLauncherScriptIsSafe,
  assertMacosTrampolineStub,
  macosTrampolineStubPath,
  npmExecFileInvocation,
  requiredEntriesForPlatform
} from './prebuild-check.mjs';

test('macOS bundles JBSMatch payload', () => {
  expect(requiredEntriesForPlatform('macos')).toEqual([
    'run_bepinex.sh',
    'libdoorstop.dylib',
    'BepInEx/plugins/BazaarPlusPlus.JBSMatch.dll',
    'BepInEx/plugins/BazaarPlusPlus.JBSMatch.version',
    'BepInEx/plugins/JBSMatch/locales/zh-CN.json',
    'BepInEx/plugins/JBSMatch/locales/en-US.json'
  ]);
});

test('Windows bundles JBSMatch payload', () => {
  expect(requiredEntriesForPlatform('windows')).toEqual([
    'winhttp.dll',
    'doorstop_config.ini',
    'BepInEx/plugins/BazaarPlusPlus.JBSMatch.dll',
    'BepInEx/plugins/BazaarPlusPlus.JBSMatch.version',
    'BepInEx/plugins/JBSMatch/locales/zh-CN.json',
    'BepInEx/plugins/JBSMatch/locales/en-US.json'
  ]);
});

test('macOS launcher check accepts safe codesign tempfile handling', () => {
  const script = [
    '_entitlements_file="$(mktemp "${TMPDIR:-/tmp}/bepinex_ents.XXXXXX")"',
    'trap cleanup_entitlements EXIT HUP INT TERM',
    'codesign --force --deep --sign - --entitlements "$_entitlements_file" "$app_path"'
  ].join('\n');

  expect(() => assertMacosLauncherScriptIsSafe(script)).not.toThrow();
});

test('macOS launcher check rejects the broken BSD mktemp template', () => {
  const script = [
    '_entitlements_file="$(mktemp /tmp/bepinex_ents.XXXXXX.plist)"',
    'trap cleanup_entitlements EXIT HUP INT TERM',
    'codesign --force --deep --sign - --entitlements "$_entitlements_file" "$app_path"'
  ].join('\n');

  expect(() => assertMacosLauncherScriptIsSafe(script)).toThrow(
    'mktemp /tmp/bepinex_ents.XXXXXX.plist'
  );
});

test('macOS launcher check rejects preemptive signature removal', () => {
  const script = [
    '_entitlements_file="$(mktemp "${TMPDIR:-/tmp}/bepinex_ents.XXXXXX")"',
    'trap cleanup_entitlements EXIT HUP INT TERM',
    'codesign --remove-signature "$app_path" 2>/dev/null || true',
    'codesign --force --deep --sign - --entitlements "$_entitlements_file" "$app_path"'
  ].join('\n');

  expect(() => assertMacosLauncherScriptIsSafe(script)).toThrow(
    'codesign --remove-signature'
  );
});

test('prebuild check invokes npm through cmd on Windows', () => {
  expect(
    npmExecFileInvocation(['run', 'generate:bindings'], 'win32', {
      ComSpec: 'C:\\Windows\\System32\\cmd.exe'
    })
  ).toEqual({
    command: 'C:\\Windows\\System32\\cmd.exe',
    args: ['/d', '/s', '/c', 'npm', 'run', 'generate:bindings']
  });
});

test('trampoline stub check fails loudly when the compiled stub is missing', () => {
  const root = mkdtempSync(path.join(tmpdir(), 'bpp-stub-'));
  expect(() => assertMacosTrampolineStub(root)).toThrow(
    'Missing compiled macOS trampoline stub'
  );
});

test('trampoline stub check rejects a non-Mach-O stub', () => {
  const root = mkdtempSync(path.join(tmpdir(), 'bpp-stub-'));
  const stubPath = macosTrampolineStubPath(root);
  mkdirSync(path.dirname(stubPath), { recursive: true });
  writeFileSync(stubPath, 'not a mach-o binary');
  expect(() => assertMacosTrampolineStub(root)).toThrow('not arm64 Mach-O');
});
