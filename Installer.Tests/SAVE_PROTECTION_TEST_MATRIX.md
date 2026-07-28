# TD Bank v0.1 / Setup v0.1 protection test matrix

All automated scenarios use a temporary fake
`%APPDATA%\SlayTheSpire2` tree. They must never read or write the developer's
real saves.

## Non-negotiable invariants

1. Vanilla saves are source-only. Their bytes, paths, and timestamps never
   change.
2. A modded profile with real progress, a current run, history, replays, or an
   unknown extra file is never overwritten.
3. A missing or game-generated blank modded profile may be initialized only
   from a usable vanilla profile.
4. Any modded profile replaced as blank is backed up before the atomic swap.
5. Reinstall is idempotent: the first migration may initialize a profile, but a
   later install must not overwrite that initialized/played profile.
6. Every migrated file is hash-equal to its source. Preserved profiles retain
   both their bytes and timestamps.
7. Enumeration is limited to
   `steam\<numeric SteamID>\profile1..3`. Reparse points and
   traversal-shaped account, profile, or transaction names cannot escape the
   injected save root.
8. A failed copy leaves the previous target byte-for-byte intact, leaves no
   partial target, and removes staging data. A recoverable backup remains
   available if rollback itself cannot finish.
9. Steam Cloud metadata (`Steam\userdata`, `remotecache.vdf`, and similar
   files) is outside the Setup migration scope and remains untouched. Setup
   writes an atomic, one-time `tdbank_migration_v2_1.pending.json` marker for
   the Mod's pre-cloud-sync guard instead.
10. Removing or upgrading the Mod must not remove vanilla, modded, or
    save-protection backup data. The uninstaller does not accept a save-root
    input and never enumerates a save path.
11. Mod backups and staging data never remain below `mods`. The game scans
    manifests recursively, so legacy `.cnj-tdbank-backups` and crashed
    `.cnj-tdbank-stage-*` directories are moved intact to the transaction
    backup outside the mod scan tree.
12. TDLib leaves `mods` only when both proofs pass: TD Bank's install state
    says this Setup owns TDLib, and the current TDLib tree is an exact
    hash/identity match for the embedded payload. Any uncertainty preserves it.
13. TD Bank and a proven Setup-owned TDLib are one uninstall transaction.
    Both move to a recovery backup outside `mods`; any failure restores both
    byte-for-byte or reports a retained recovery backup if rollback is blocked.
14. Ordinary `mods\BaseLib` is outside the dependency transaction. Install and
    uninstall must succeed while its DLL is exclusively locked, and its complete
    tree must remain byte-, attribute-, path-, and timestamp-identical.

## Automated scenarios

| ID | Setup | Expected result |
| --- | --- | --- |
| SP-01 | Usable vanilla profile; modded profile absent | `Migrated`; complete directory copied and verified; vanilla unchanged |
| SP-02 | Usable vanilla profile; modded directory exists but is empty | `Migrated`; old empty directory backed up |
| SP-03 | Usable vanilla profile; modded contains only a parseable zero-progress `progress.save`, its backup, and prefs | `Migrated`; blank skeleton backed up before replacement |
| SP-04 | Modded progress contains any real progress metric | `PreservedEstablished`; no bytes or timestamps change |
| SP-05 | Modded zero-progress save also has history, a current run, replay, or unknown file | `PreservedEstablished`; evidence of use wins over the blank heuristic |
| SP-06 | Vanilla and modded profiles are already byte-equivalent | `AlreadyEquivalent`; no profile write or timestamp churn; verified backup and cloud-handoff marker remain available |
| SP-07 | Vanilla profile is missing, empty, malformed, or lacks usable progress | `NoUsableVanilla`; no target is created or changed |
| SP-08 | Two numeric Steam accounts with multiple `profileN` directories and mixed states | Every valid profile gets its own correct disposition; no cross-account copy |
| SP-09 | Non-numeric account names and invalid profile names (`profile0`, suffixes, traversal-shaped names) | Ignored or `SkippedUnsafe`; sentinels remain untouched |
| SP-10 | Vanilla account has root `profile.save` and modded root does not | Root file is copied once and included as a critical marker entry |
| SP-11 | Modded root already has `profile.save` | Existing root file is preserved, even if different from vanilla |
| SP-12 | Run migration twice, changing vanilla between calls | Second call preserves established modded data and does not duplicate backups |
| SP-13 | Source secondary file is locked so staging copy fails | No partial target/swap; previous target and source remain exact; staging cleaned |
| SP-14 | Modded profile or account is a reparse point to an external sentinel directory | Rejected/`SkippedUnsafe`; external directory is unchanged |
| SP-15 | Transaction ID contains separators, rooted syntax, `..`, or invalid filename characters | Rejected before writes; no backup/stage escapes the save root |
| SP-16 | Fake Steam Cloud `userdata\...\remotecache.vdf` exists beside the save tree | Cloud metadata hash and timestamp remain unchanged |
| SP-17 | Successful migration or a complete byte-equivalent local mirror | Atomic pending marker lists all mirrored files and SHA-256 hashes; `profile.save` and every `progress.save` are marked critical |
| SP-18 | Established/unknown modded data is preserved | Setup does not create a marker that could authorize a cloud upload over that data |
| SP-19 | Reinstall while a valid pending marker exists | Marker remains valid and idempotent; no profile or remote metadata is rewritten |
| SP-20 | Simulated Mod removal after installation | Both save namespaces and backup tree remain byte-for-byte intact |
| UN-01 | Setup v0.1 detects an installed TD Bank; consent is unchecked | Red uninstall action remains enabled; install remains disabled |
| UN-02 | Fresh Setup install owns an exact embedded TDLib payload | `mods\TDBank` and `mods\TDLib` both move to separately audited recovery-backup directories outside `mods` |
| UN-03 | Vanilla, modded, Cloud-shaped, and backup save fixtures exist for every uninstall ownership/failure case | Every save-tree byte, attribute, path, and timestamp remains identical; the uninstaller receives and accesses no save root |
| UN-04 | Run uninstall again after a successful uninstall | `AlreadyAbsent`; no backup or save is created or changed |
| UN-05 | Installed Mod has user-replaced artwork | Artwork is retained in the recovery backup outside `mods` |
| UN-06 | Public-beta version has advanced beyond the installer's supported version | Existing recognized TD Bank can still be uninstalled safely |
| UN-07 | TD Bank DLL is locked | Uninstall stops before the move; Mod and saves remain exact |
| UN-08 | `mods\TDBank` is unrelated or cannot prove TD Bank identity | Refused before the move; directory remains exact |
| UN-09 | Failure after TD Bank and managed TDLib have moved | Both directory inventories are restored exactly and a dedicated rollback-completed result is reported |
| UN-10 | Failure plus deliberate TD Bank rollback-path collision | Managed TDLib is still restored exactly; rollback-failed reports the retained TD Bank recovery backup |
| UN-11 | TD Bank root or a nested entry is a symbolic link/junction | Refused; external sentinel contents remain unchanged |
| UN-12 | TDLib was already installed and exactly matches the payload before Setup runs | Install state records `managedBySetup: false`; uninstall removes TD Bank but preserves TDLib byte-, attribute-, and timestamp-identical |
| UN-13 | A newer TDLib was already installed | `PreserveNewer`; ownership remains false and uninstall preserves every TDLib byte and timestamp |
| UN-14 | Setup originally installed TDLib, but a file was changed or an extra entry was added later | Exact-payload proof fails; TD Bank is removed and TDLib is preserved exactly |
| UN-15 | Setup originally installed TDLib, but it was subsequently upgraded | Version/hash proof fails; the upgraded TDLib is preserved exactly |
| UN-16 | TDLib cannot be hashed, ownership JSON is malformed, its proof hash is wrong, or ownership cannot otherwise be proved | Conservative preservation; TD Bank can still be removed |
| UN-17 | An action-only legacy state has no `tdLibOwnership` proof | Metadata remains readable but cannot prove ownership; TDLib is preserved |
| UN-18 | A v2.5 state contains `baseLibAction`/`baseLibOwnership`, and ordinary BaseLib is exclusively locked | TD Bank is removed; BaseLib remains exactly unchanged and is never treated as TDLib |
| UN-19 | Current ownership state has schema 1, `managedBySetup: true`, exact payload version, exact relative-file/hash proof, TDLib manifest identity, and an exact current tree | TDLib is eligible for transactional removal; every field/check is required |
| UN-20 | Ordinary BaseLib is present and exclusively locked during a normal v0.1 install and uninstall | Both transactions succeed; BaseLib remains exactly unchanged |

## Integration and release checks

- `TransactionInstaller.Install` deploys and audits v0.1 first, then invokes
  save protection as the final operation allowed to fail. Once the marker is
  committed, a best-effort status write cannot roll the v0.1 cloud guard back
  to an older DLL.
- A save-protection failure cannot make an otherwise valid established modded
  profile disappear. The UI must give the backup path when manual recovery is
  needed.
- The embedded Mod DLL, manifests, and payload summaries consistently report
  TD Bank `v0.1`; the executable filename, assembly metadata, and
  install-state `installerVersion` consistently report Setup `v0.1`.
- The one-time Cloud migration handshake deliberately retains protocol
  `release_version: 2.2.0`, allowing a marker created before the public v0.1 update
  to finish safely instead of being abandoned mid-upload.
- Fresh install, reinstall, locked-DLL rollback, ordinary-BaseLib isolation,
  and newer-TDLib preservation continue to pass the transactional
  suite.
- Reinstall relocates legacy backup/staging trees out of `mods`, retains their
  sentinels, and leaves exactly one scan-visible `TDBank.json`. A later
  save-protection failure restores both the previous Mod and any relocated
  legacy tree as part of the outer rollback.
- Fresh installs and repairs write `tdLibOwnership` schema 1 with the
  ownership decision, action, payload version, and the exact relative-file
  SHA-256 set. Reinstall carries ownership forward only when the previous state
  proves ownership and the active TDLib is still the exact payload.
- Legacy BaseLib metadata can still identify an old TD Bank installation, but
  never authorizes access to `mods\BaseLib`. Missing/malformed TDLib state,
  unknown ownership schemas, extra entries, changed hashes, unreadable files,
  and reparse points all preserve TDLib.
- Before the game's `DoCloudSync`, the Mod consumes the pending marker and
  verifies all listed local hashes. A mismatch invalidates the authorization.
- If the remote modded namespace is absent or provably game-generated blank,
  the pre-sync guard forces one local upload from the verified marker. If the
  remote namespace is established, malformed, or unknown, remote data remains
  authoritative and no forced upload occurs.
- The marker is cleared only after a confirmed successful upload, or retired
  safely when established remote data wins. A crash or sync failure leaves a
  retryable marker and does not create an upload loop.
- Release testing is performed with the game fully closed. Test first launch
  once with a missing/blank remote namespace and once with an established
  remote namespace.

## Known boundary

Changing local timestamps does **not** protect these saves. Reverse engineering
of the current public-beta `CloudSaveStore` shows that a differing timestamp
can cause the cloud copy to overwrite local data, while a missing remote entry
can delete the local file. Setup therefore cannot solve this by touching files:
the one-time marker and Mod-side pre-sync guard are required. Backups must remain
recoverable, and user-facing text must not claim that Setup controls Steam
Cloud outside that guarded first-sync handoff.

Uninstall itself has no save-root parameter and never reads or edits save
files, so all save namespaces remain byte-for-byte unchanged when the button
returns. It moves only a recognized TD Bank and, when every ownership and exact
payload check passes, this Setup's TDLib. TD Bank's account field is
registered by the TD Bank DLL, however; if the user later launches and resaves
without the Mod, preservation of that unknown extension field is controlled by
the game serializer rather than by the uninstaller. UI copy must not
promise more than the uninstall transaction can guarantee.
