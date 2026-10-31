# Conformance boundaries

The public contribution contract is [spec1018 contributions](../../1018-weaver-module-contributions/contracts/contributions.md). This file defines executable witness boundaries without another generic API.

## Independent resource author

Backend package references only public Agent.Core, Preferences.Core, Identity.Core and CShells/ASP abstractions. Scoped implementations own context admission, read/preparation/application/risk and result semantics. Studio package references public Studio.Core/CShells; its prebuilt browser module is compiled against the real SDK tgz and shared React. No product source imports, aliases, friend access or common implementation copies.

Module configuration supplies one exact Studio host ID. Both tools derive current personal key/authority. Read is side-effect-free. Visibility change requires review, exact owner/revision, native validation/CAS and owner readback. Only own-widget data is exposed to provider payloads. Typed renderers are module-owned; absent renderers use generic safe presentation without interpreting domain fields.

Fixture identities: module `Acme.Home`; backend package/assembly `Acme.Home.Weaver` with feature `AcmeHomeWidget`; Studio package `Acme.Home.Studio`, assembly/module `Acme.Home`, feature `AcmeHomeStudio`; widget `acme.home:status`; resource type `home-widget`; route/scope `/qualification/acme-home`; authorized owning snapshot endpoint `/_qualification/acme/home-widget`; context type `acme.home.widget`; tools `acme_home_read_widget` (empty object) and `acme_home_set_visibility` (exactly one boolean `visible`). Targets use the widget ID and real personal document revision. Read result type `acme.home.widget-state`; reviewed change/preview type `acme.home.widget-visibility`; renderer `acme.home.widget-result`, all schema/contract version `1`. Display the widget as “Acme status card.” These values belong to the external author/driver, never common runtime branches.

## Protocol fixture

Separate package; no resource assembly/frontend dependency or resource/type/tool-name branch. Use installed descriptors and arbitrary JSON tool arguments through the unchanged provider adapters. One active provider per run. All protocol observations are deterministic, explicitly not live-provider evidence.

## Qualifier inputs/outputs

Accept a new owned output directory and a unique local candidate version; optional supplied artifacts require explicit immutable producer metadata. Refuse source/driver/artifact identity mismatch. Retain producer and external author sources, lock/restore graphs, archives, published host files and normal auth evidence privately. Redact tokens/credentials from public receipts and provider trace artifacts.

A clean consumer restores/builds/publishes with Node/npm/pnpm/npx denied and audited; browser automation and module production run outside that denied environment. At runtime the consumer cannot use product sources or build caches. The ordinary backend feed/state and the protected Studio feed/coordinated-store enrollment are explicit isolated fixture inputs, following the [host recipes](hosts.md). Stop only verified owned processes.

Freeze common DLL/browser identities before extension. Configuration/resource/module membership may change; common implementation bytes may not. Record actual generation and contribution identity for each transition.

## Required observations

- Real module context/read → reviewed visibility mutation → native personal document plus Home reload; native workflow layout/validation/whole undo and explicit Save.
- Both unchanged provider adapters through their protocol clients; no module semantics in providers/common components.
- Current read/write authority, cross-user/tenant/host scope, stale revision and true concurrent CAS, foreign/malformed/no-op inputs and unrelated-data redaction.
- Denial/cancellation before effect; duplicate execute; possible postcommit uncertainty without replay; malformed result/removed originating client refuse false success.
- Actual package add/remove/restore, pending operations against removed contributions, consumed intent remount, genuine SDK incompatibility and restored repeat.
- Retained exact common artifact comparison and distribution-only consumer closure.

Every control retains its real failed observation and exact recognized cause. Unrun cases, skips, wrong-cause failures or transport success without owner confirmation fail aggregate acceptance. Supported retention/recovery limits remain explicit; M2–M5 own broader qualification.

## Contract decision

Record demonstrated seams, any minimal correction, preview package/API migration and final repeated artifact evidence. Accept/freeze only after accepted #3437 integration and the complete witness, then normal exact-head review/CI and resulting-main gates. Do not infer Program completion.
