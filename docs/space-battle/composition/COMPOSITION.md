English | [Русский](COMPOSITION.ru.md)

# Logical entity composition

These are separate diagnostic replays, not performance measurements. Values describe the recorded workload and physics revision; historical data do not describe current gameplay performance. The observer counts logical gameplay components, excluding adapter bookkeeping.

An entity-tick means one live entity at the end of one simulation tick. Components per entity is ComponentTicks / EntityTicks, weighted by population and lifetime. MeanOfPerTickMeans is the unweighted mean of each tick's ratio. API calls and structural operations include short-lived objects that can disappear within a tick. Payload bytes exclude storage alignment and ECS/physics overhead.

All recorded numeric census, type, group, histogram and signature fields are reproduced below. Identifiers remain identical across languages. JSON conservation checks are mandatory; available raw CSV and source results are additionally checked against their recorded hashes and aggregates.

## low

Ticks: 108000; checksum: `BC1ED478AF94A722`.

Separate diagnostic replay, every end-of-Step tick 1..108000; setup excluded from sums and API operations; no performance measurement

### Census summary

| Field | Value |
| --- | --- |
| EntityTicks | 94570168 |
| ComponentTicks | 340555644 |
| MeanEntities | 875.6497037037037 |
| MeanComponents | 3153.293 |
| MeanComponentsPerEntity | 3.601089552891563 |
| MeanOfPerTickMeans | 3.6777257902707765 |
| MinEntities | 118 |
| MaxEntities | 4898 |
| MinComponents | 826 |
| MaxComponents | 16651 |
| ObservedCompositions | 64 |

### Components and API activity

| Name | LogicalPayloadBytes | Initial | Final | MeanActive | MinActive | MaxActive | EntityTicks | EntityPresencePercent | AddsDuringSimulation | RemovesDuringSimulation | GetCalls | HasCalls | QueryBindings |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Identity | 12 | 198 | 49 | 47.5715462962963 | 24 | 198 | 5137727 | 5.432714257206353 | 1201 | 1350 | 6463881 | 11916262 | 35659517 |
| Transform | 20 | 198 | 1316 | 875.6497037037037 | 118 | 4898 | 94570168 | 100 | 3156864 | 3155746 | 52757615 | 0 | 110931448 |
| Motion | 32 | 198 | 1316 | 875.6497037037037 | 118 | 4898 | 94570168 | 100 | 3156864 | 3155746 | 10968236 | 0 | 94972569 |
| Hull | 12 | 198 | 49 | 47.5715462962963 | 24 | 198 | 5137727 | 5.432714257206353 | 1201 | 1350 | 22776062 | 0 | 5529450 |
| Shield | 20 | 198 | 49 | 47.5715462962963 | 24 | 198 | 5137727 | 5.432714257206353 | 1201 | 1350 | 14312910 | 0 | 5137876 |
| Heat | 12 | 198 | 49 | 47.5715462962963 | 24 | 198 | 5137727 | 5.432714257206353 | 1201 | 1350 | 9512684 | 0 | 5137876 |
| Brain | 28 | 198 | 49 | 47.5715462962963 | 24 | 198 | 5137727 | 5.432714257206353 | 1201 | 1350 | 57175 | 0 | 19309064 |
| Weapon | 32 | 198 | 49 | 47.5715462962963 | 24 | 198 | 5137727 | 5.432714257206353 | 1201 | 1350 | 3938053 | 0 | 5137876 |
| SecondaryWeapon | 32 | 102 | 39 | 36.068268518518515 | 16 | 102 | 3895373 | 4.119029375098498 | 563 | 626 | 0 | 1350 | 3895436 |
| Collider | 12 | 198 | 49 | 47.5715462962963 | 24 | 198 | 5137727 | 5.432714257206353 | 1201 | 1350 | 290754 | 0 | 0 |
| PhysicsLink | 4 | 198 | 49 | 47.5715462962963 | 24 | 198 | 5137727 | 5.432714257206353 | 1201 | 1350 | 9885528 | 0 | 0 |
| CombatState | 4 | 198 | 49 | 47.5715462962963 | 24 | 198 | 5137727 | 5.432714257206353 | 1201 | 1350 | 665294 | 0 | 0 |
| Carrier | 20 | 6 | 6 | 5.05 | 3 | 6 | 545400 | 0.5767146358458409 | 57 | 57 | 3897 | 1350 | 545400 |
| Docked | 12 | 0 | 0 | 3.6256851851851852 | 0 | 19 | 391574 | 0.41405657648826427 | 1459 | 1459 | 8900 | 24857619 | 391574 |
| Overheated | 4 | 0 | 3 | 5.319462962962963 | 0 | 21 | 574502 | 0.6074875535803215 | 1144 | 1141 | 0 | 7272660 | 0 |
| InCombat | 4 | 0 | 40 | 39.27525 | 0 | 183 | 4241727 | 4.48526960425829 | 5573 | 5533 | 4241687 | 5808703 | 0 |
| Boosting | 4 | 0 | 6 | 3.3500185185185187 | 0 | 31 | 361802 | 0.3825751900958873 | 11722 | 11716 | 361796 | 9908877 | 0 |
| ShieldBroken | 4 | 0 | 4 | 5.511287037037037 | 0 | 53 | 595219 | 0.6293940389320235 | 5622 | 5618 | 0 | 5370836 | 0 |
| Projectile | 36 | 0 | 167 | 85.77433333333333 | 0 | 422 | 9263628 | 9.795507606584774 | 403519 | 403352 | 9666980 | 0 | 28194069 |
| Debris | 16 | 0 | 213 | 237.94272222222222 | 3 | 1192 | 25697814 | 27.173277306644945 | 263747 | 263534 | 25926963 | 0 | 77093016 |
| Particle | 32 | 0 | 887 | 504.36110185185186 | 0 | 4235 | 54470999 | 57.598500829563925 | 2488397 | 2487510 | 56917615 | 0 | 163411223 |
| Lifetime | 4 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| Inventory | 136 | 198 | 49 | 47.5715462962963 | 24 | 198 | 5137727 | 5.432714257206353 | 1201 | 1350 | 1454 | 0 | 5137876 |

### Entity groups

| Name | EntityTicks | ComponentTicks | MeanEntities | MeanComponentsPerEntity | MinEntities | MaxEntities | EntitySharePercent |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Escorts | 4592327 | 63655289 | 42.52154629629629 | 13.861227434370418 | 18 | 192 | 4.855999621360512 |
| Carriers | 545400 | 8603032 | 5.05 | 15.773802713604693 | 3 | 6 | 0.5767146358458409 |
| Projectiles | 9263628 | 27790884 | 85.77433333333333 | 3 | 0 | 422 | 9.795507606584774 |
| Debris | 25697814 | 77093442 | 237.94272222222222 | 3 | 3 | 1192 | 27.173277306644945 |
| Particles | 54470999 | 163412997 | 504.36110185185186 | 3 | 0 | 4235 | 57.598500829563925 |
| Unclassified | 0 | 0 | 0 | n/a | 0 | 0 | 0 |

### Component-count histogram

| Components | EntityTicks | Percent |
| --- | --- | --- |
| 3 | 89432441 | 94.56728574279364 |
| 12 | 229391 | 0.2425616923933137 |
| 13 | 1197130 | 1.2658643051157528 |
| 14 | 2281704 | 2.4127101053685345 |
| 15 | 952075 | 1.006739249950365 |
| 16 | 398301 | 0.42116981329672587 |
| 17 | 79126 | 0.08366909108166118 |

### All observed signatures

| Components | ComponentCount | EntityTicks | Percent |
| --- | --- | --- | --- |
| Transform + Motion + Particle | 3 | 54470999 | 57.598500829563925 |
| Transform + Motion + Debris | 3 | 25697814 | 27.173277306644945 |
| Transform + Motion + Projectile | 3 | 9263628 | 9.795507606584774 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + InCombat + Inventory | 14 | 1894887 | 2.0036836563513347 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + InCombat + Inventory | 13 | 683526 | 0.7227712654586804 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Inventory | 13 | 455924 | 0.48210129012354086 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + Overheated + InCombat + Inventory | 16 | 251287 | 0.265714871099732 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + InCombat + ShieldBroken + Inventory | 15 | 247767 | 0.26199276710600744 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Inventory | 12 | 229391 | 0.2425616923933137 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + InCombat + Boosting + Inventory | 15 | 177770 | 0.187976825842162 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + InCombat + Inventory | 15 | 173012 | 0.18294564095519 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + InCombat + Inventory | 15 | 163143 | 0.1725100033659663 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Overheated + InCombat + Inventory | 15 | 154828 | 0.16371759009669942 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + InCombat + ShieldBroken + Inventory | 14 | 123272 | 0.13034977372568482 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + Inventory | 14 | 77275 | 0.08171181423723388 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + Overheated + InCombat + ShieldBroken + Inventory | 17 | 72249 | 0.07639724188710334 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + InCombat + Boosting + Inventory | 14 | 63882 | 0.06754984299065642 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + InCombat + Inventory | 14 | 49050 | 0.051866250253462594 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Overheated + InCombat + ShieldBroken + Inventory | 16 | 43479 | 0.045975386233849135 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Boosting + Inventory | 14 | 40640 | 0.04297338247300142 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + Inventory | 13 | 32702 | 0.034579615000789675 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + InCombat + Boosting + ShieldBroken + Inventory | 16 | 24356 | 0.025754421838396227 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Boosting + Inventory | 13 | 20937 | 0.02213911685130981 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + InCombat + ShieldBroken + Inventory | 16 | 20541 | 0.021720380152015804 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + InCombat + ShieldBroken + Inventory | 16 | 20402 | 0.021573399340899975 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + Overheated + InCombat + Inventory | 16 | 19300 | 0.020408127011046443 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + InCombat + Boosting + Inventory | 16 | 15038 | 0.015901420414099296 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + Inventory | 14 | 14520 | 0.015353678974113697 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + InCombat + Boosting + ShieldBroken + Inventory | 15 | 11769 | 0.012444727813109098 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Overheated + Inventory | 14 | 10207 | 0.010793044165893837 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + InCombat + ShieldBroken + Inventory | 15 | 10155 | 0.010738058538713815 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Overheated + InCombat + Inventory | 14 | 5382 | 0.005691012413132226 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Overheated + InCombat + ShieldBroken + Inventory | 15 | 4305 | 0.004552175480961396 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + ShieldBroken + Inventory | 13 | 4041 | 0.004273017681432056 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + Overheated + InCombat + ShieldBroken + Inventory | 17 | 4030 | 0.004261386106451667 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + Overheated + Inventory | 15 | 3154 | 0.003335089771649766 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + Overheated + InCombat + Inventory | 15 | 2323 | 0.00245637715267673 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + ShieldBroken + Inventory | 14 | 2241 | 0.0023696690482774654 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + InCombat + Boosting + ShieldBroken + Inventory | 17 | 1829 | 0.0019340136944665256 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + Boosting + Inventory | 15 | 1413 | 0.0014941286770263535 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + Overheated + ShieldBroken + Inventory | 16 | 1063 | 0.0011240331094685165 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Overheated + InCombat + Boosting + Inventory | 16 | 1010 | 0.001067990066381187 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + ShieldBroken + Inventory | 15 | 905 | 0.0009569613961138358 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + InCombat + Boosting + Inventory | 16 | 846 | 0.0008945738575826575 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + Overheated + InCombat + ShieldBroken + Inventory | 16 | 752 | 0.0007951767622956956 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + InCombat + Boosting + ShieldBroken + Inventory | 17 | 622 | 0.0006577126943456418 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + Overheated + Inventory | 15 | 328 | 0.0003468324175970587 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Boosting + ShieldBroken + Inventory | 14 | 310 | 0.0003277989312655128 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Boosting + ShieldBroken + Inventory | 15 | 262 | 0.00027704296771472375 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Overheated + ShieldBroken + Inventory | 15 | 257 | 0.0002717558881781832 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + InCombat + Boosting + Inventory | 15 | 238 | 0.0002516649859393292 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + ShieldBroken + Inventory | 15 | 220 | 0.0002326314996077833 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + Overheated + InCombat + Boosting + Inventory | 17 | 209 | 0.00022099992462739412 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + InCombat + Boosting + ShieldBroken + Inventory | 16 | 164 | 0.00017341620879852935 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Overheated + InCombat + Boosting + ShieldBroken + Inventory | 17 | 162 | 0.00017130137698391314 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Overheated + InCombat + Boosting + Inventory | 15 | 113 | 0.00011948799752581597 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + Boosting + Inventory | 15 | 78 | 8.247844077003225E-05 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + Boosting + ShieldBroken + Inventory | 16 | 49 | 5.181337945809719E-05 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Overheated + Boosting + Inventory | 15 | 35 | 3.7009556755783706E-05 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + Boosting + Inventory | 14 | 31 | 3.2779893126551285E-05 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + Overheated + InCombat + Boosting + Inventory | 17 | 25 | 2.6435397682702646E-05 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + Boosting + ShieldBroken + Inventory | 16 | 10 | 1.0574159073081058E-05 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + ShieldBroken + Inventory | 14 | 7 | 7.401911351156741E-06 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + Overheated + InCombat + Boosting + Inventory | 16 | 4 | 4.229663629232424E-06 |

### Checkpoint phases

| Tick | Checksum | NativeComponentCountsVerified |
| --- | --- | --- |
| 3600 | 4C65101D2CE6AD1D | true |
| 7200 | 03D31115A19EE4BF | true |
| 10800 | 074BDAA5C5DAA100 | true |
| 14400 | C4F12B976EEB2D32 | true |
| 18000 | 97B950BAF8AC8551 | true |
| 21600 | 48853BA3C50A2FDE | true |
| 25200 | 5C2C84307CB61FBB | true |
| 28800 | 14653CF8C4477648 | true |
| 32400 | 20F72219E969E33F | true |
| 36000 | F9EBB20C0CB56C90 | true |
| 39600 | 694CCA1E9AD2A37F | true |
| 43200 | 897258B2B84113C6 | true |
| 46800 | 5EB2F8A85206FDC9 | true |
| 50400 | 44A3D6924AFE3E4F | true |
| 54000 | A67A89B13C604EF5 | true |
| 57600 | 91190B5785FB1141 | true |
| 61200 | 1224BEA3F63F3820 | true |
| 64800 | 1D5466D9FB8F6FB1 | true |
| 68400 | 1C028979175E272F | true |
| 72000 | B469536A0494E85A | true |
| 75600 | 2C3A54DF57962262 | true |
| 79200 | AA3C6ECC97090700 | true |
| 82800 | EB515A8C44C55FA3 | true |
| 86400 | BB68264984A056D0 | true |
| 90000 | 5DAD372E7A71D662 | true |
| 93600 | E7E97722A3A99BA4 | true |
| 97200 | 0E0A4DB94B4E69D0 | true |
| 100800 | F4A67E0E91BCD3A1 | true |
| 104400 | 530C58992DF9AEDC | true |
| 108000 | BC1ED478AF94A722 | true |

### Recorded configuration

| Field | Value |
| --- | --- |
| Seed | 7331 |
| ShipsPerTeam | 96 |
| CarriersPerTeam | 3 |
| ArenaHalfSize | 210 |
| ChurnMultiplier | 0.5 |
| TickRate | 60 |
| ReplenishCarriers | true |
| ParticleMultiplier | 1 |
| MaxProjectiles | 24000 |
| MaxDebris | 16000 |
| MaxParticles | 32000 |

### Recorded provenance

| Field | Value |
| --- | --- |
| SchemaVersion | 1 |
| SimulatedSeconds | 1800 |
| SourceResult | results/low-secs.json |
| SourceSha256 | 5E01CABF66F78CDDF469A5F1B05F16D4AFCE42CBE04A81989ED015D3A8957D52 |
| TickCsvSha256 | 41EC0F31677109ED9DE89355AE18F62A259DF2A228FDF7A385AA6BE4EB9F57F3 |
| ObserverSha256 | D074B983423F90D4E976B5E2A5FA6003F1CEF0EC2D37E778BF0F8C255A26EE79 |
| BinaryHashes.SpaceBattle.Core.dll | C33F0C46BD5725E342233BF456E2EACF412FDDAD2EF91C43827E513D3E9D4120 |
| BinaryHashes.SpaceBattle.SECS.dll | 5896308C178CE8FD87680540B70CAB72CEFF4D37EECC704C761B1FC17990E0D8 |
| BinaryHashes.SECS.dll | 3848BFF3E9D65815AEC89B2B3E746215E5AF94CDFBA82F87CD5D40C8A4358AFF |
| BinaryHashes.Aether.Physics2D.dll | A35C71A9BACA513C0B50BBE29EBCED17719DE0349115C4C32A88F6F09AE0C121 |

## nominal

Ticks: 108000; checksum: `374773A7D7A76049`.

Separate diagnostic replay, every end-of-Step tick 1..108000; setup excluded from sums and API operations; no performance measurement

### Census summary

| Field | Value |
| --- | --- |
| EntityTicks | 119952471 |
| ComponentTicks | 416595267 |
| MeanEntities | 1110.6710277777777 |
| MeanComponents | 3857.3635833333333 |
| MeanComponentsPerEntity | 3.473002794581864 |
| MeanOfPerTickMeans | 3.630010049922361 |
| MinEntities | 136 |
| MaxEntities | 6636 |
| MinComponents | 1015 |
| MaxComponents | 21918 |
| ObservedCompositions | 71 |

### Components and API activity

| Name | LogicalPayloadBytes | Initial | Final | MeanActive | MinActive | MaxActive | EntityTicks | EntityPresencePercent | AddsDuringSimulation | RemovesDuringSimulation | GetCalls | HasCalls | QueryBindings |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Identity | 12 | 198 | 66 | 47.3034537037037 | 26 | 198 | 5108773 | 4.25899771585364 | 2063 | 2195 | 6641097 | 11376411 | 35167874 |
| Transform | 20 | 198 | 136 | 1110.6710277777777 | 136 | 6636 | 119952471 | 100 | 3809835 | 3809897 | 67436622 | 0 | 136218128 |
| Motion | 32 | 198 | 136 | 1110.6710277777777 | 136 | 6636 | 119952471 | 100 | 3809835 | 3809897 | 10966988 | 0 | 120413969 |
| Hull | 12 | 198 | 66 | 47.3034537037037 | 26 | 198 | 5108773 | 4.25899771585364 | 2063 | 2195 | 22270964 | 0 | 5661970 |
| Shield | 20 | 198 | 66 | 47.3034537037037 | 26 | 198 | 5108773 | 4.25899771585364 | 2063 | 2195 | 14167297 | 0 | 5108905 |
| Heat | 12 | 198 | 66 | 47.3034537037037 | 26 | 198 | 5108773 | 4.25899771585364 | 2063 | 2195 | 9095555 | 0 | 5108905 |
| Brain | 28 | 198 | 66 | 47.3034537037037 | 26 | 198 | 5108773 | 4.25899771585364 | 2063 | 2195 | 96785 | 0 | 18810650 |
| Weapon | 32 | 198 | 66 | 47.3034537037037 | 26 | 198 | 5108773 | 4.25899771585364 | 2063 | 2195 | 2957703 | 0 | 5108905 |
| SecondaryWeapon | 32 | 102 | 45 | 32.25812962962963 | 16 | 102 | 3483878 | 2.904382019775149 | 963 | 1020 | 0 | 2195 | 3483935 |
| Collider | 12 | 198 | 66 | 47.3034537037037 | 26 | 198 | 5108773 | 4.25899771585364 | 2063 | 2195 | 357643 | 0 | 0 |
| PhysicsLink | 4 | 198 | 66 | 47.3034537037037 | 26 | 198 | 5108773 | 4.25899771585364 | 2063 | 2195 | 9666940 | 0 | 0 |
| CombatState | 4 | 198 | 66 | 47.3034537037037 | 26 | 198 | 5108773 | 4.25899771585364 | 2063 | 2195 | 819229 | 0 | 0 |
| Carrier | 20 | 6 | 3 | 4.421972222222222 | 2 | 6 | 477573 | 0.3981351913959321 | 92 | 95 | 8078 | 2195 | 477576 |
| Docked | 12 | 0 | 0 | 5.120972222222222 | 0 | 21 | 553065 | 0.46107011834712436 | 2169 | 2169 | 29594 | 24151606 | 553065 |
| Overheated | 4 | 0 | 1 | 8.989546296296297 | 0 | 23 | 970871 | 0.8093797417478794 | 2761 | 2760 | 0 | 7715749 | 0 |
| InCombat | 4 | 0 | 0 | 37.335287037037034 | 0 | 175 | 4032211 | 3.361507242314333 | 6166 | 6166 | 4032211 | 5934300 | 0 |
| Boosting | 4 | 0 | 11 | 5.665518518518518 | 0 | 59 | 611876 | 0.5100987040108578 | 10172 | 10161 | 611865 | 9687017 | 0 |
| ShieldBroken | 4 | 0 | 1 | 5.8279907407407405 | 0 | 70 | 629423 | 0.5247269979123648 | 7441 | 7440 | 0 | 5449187 | 0 |
| Projectile | 36 | 0 | 0 | 106.95390740740741 | 0 | 391 | 11551022 | 9.62966573652326 | 461436 | 461436 | 12012458 | 0 | 35114502 |
| Debris | 16 | 0 | 70 | 353.39327777777777 | 7 | 1436 | 38166474 | 31.81799731328586 | 373732 | 373662 | 38484230 | 0 | 114499282 |
| Particle | 32 | 0 | 0 | 603.0203888888889 | 0 | 5868 | 65126202 | 54.29333923433724 | 2972604 | 2972604 | 68032325 | 0 | 195378606 |
| Lifetime | 4 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| Inventory | 136 | 198 | 66 | 47.3034537037037 | 26 | 198 | 5108773 | 4.25899771585364 | 2063 | 2195 | 2158 | 0 | 5108905 |

### Entity groups

| Name | EntityTicks | ComponentTicks | MeanEntities | MeanComponentsPerEntity | MinEntities | MaxEntities | EntitySharePercent |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Escorts | 4631200 | 64459630 | 42.88148148148148 | 13.918558904819486 | 20 | 192 | 3.860862524457708 |
| Carriers | 477573 | 7604543 | 4.421972222222222 | 15.923310153630963 | 2 | 6 | 0.3981351913959321 |
| Projectiles | 11551022 | 34653066 | 106.95390740740741 | 3 | 0 | 391 | 9.62966573652326 |
| Debris | 38166474 | 114499422 | 353.39327777777777 | 3 | 7 | 1436 | 31.81799731328586 |
| Particles | 65126202 | 195378606 | 603.0203888888889 | 3 | 0 | 5868 | 54.29333923433724 |
| Unclassified | 0 | 0 | 0 | n/a | 0 | 0 | 0 |

### Component-count histogram

| Components | EntityTicks | Percent |
| --- | --- | --- |
| 3 | 114843698 | 95.74100228414636 |
| 12 | 324552 | 0.27056716488983373 |
| 13 | 1284086 | 1.070495663236483 |
| 14 | 1731644 | 1.4436084438810768 |
| 15 | 1158178 | 0.9655307559274873 |
| 16 | 514767 | 0.42914247260483696 |
| 17 | 95355 | 0.07949398558033874 |
| 18 | 191 | 0.0001592297335833957 |

### All observed signatures

| Components | ComponentCount | EntityTicks | Percent |
| --- | --- | --- | --- |
| Transform + Motion + Particle | 3 | 65126202 | 54.29333923433724 |
| Transform + Motion + Debris | 3 | 38166474 | 31.81799731328586 |
| Transform + Motion + Projectile | 3 | 11551022 | 9.62966573652326 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + InCombat + Inventory | 14 | 1157987 | 0.9653715261939039 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + InCombat + Inventory | 13 | 746050 | 0.6219546740308501 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Inventory | 13 | 423786 | 0.35329493128991063 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Overheated + InCombat + Inventory | 15 | 375023 | 0.3126429967416011 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Inventory | 12 | 324552 | 0.27056716488983373 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + Overheated + InCombat + Inventory | 16 | 267584 | 0.22307502110565108 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + InCombat + Inventory | 15 | 230380 | 0.19205940326148013 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + InCombat + Boosting + Inventory | 15 | 222745 | 0.18569438223577736 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + InCombat + ShieldBroken + Inventory | 15 | 178810 | 0.1490673751939633 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + InCombat + Boosting + Inventory | 14 | 149988 | 0.1250395250298762 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + InCombat + ShieldBroken + Inventory | 14 | 125877 | 0.10493906373967068 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Overheated + InCombat + ShieldBroken + Inventory | 16 | 107858 | 0.08991728065360154 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + Inventory | 14 | 84216 | 0.0702078075573783 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + InCombat + Inventory | 14 | 80180 | 0.06684314156396161 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Boosting + Inventory | 14 | 79400 | 0.0661928840132022 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + Overheated + InCombat + ShieldBroken + Inventory | 17 | 77663 | 0.06474481046747257 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + InCombat + Inventory | 15 | 77013 | 0.06420292917517306 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Boosting + Inventory | 13 | 58838 | 0.04905109457895202 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + Inventory | 13 | 51658 | 0.043065390457858926 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + Overheated + InCombat + Inventory | 16 | 45296 | 0.03776162310153661 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + InCombat + Boosting + ShieldBroken + Inventory | 16 | 35251 | 0.029387472976692575 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + InCombat + Boosting + ShieldBroken + Inventory | 15 | 24745 | 0.0206290039660792 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Overheated + InCombat + Inventory | 14 | 20845 | 0.017377716212282113 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + InCombat + ShieldBroken + Inventory | 16 | 20205 | 0.016844171555248744 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Overheated + Inventory | 14 | 18949 | 0.01579709016582076 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + InCombat + Boosting + Inventory | 16 | 14401 | 0.01200558844677739 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + InCombat + ShieldBroken + Inventory | 15 | 12963 | 0.010806780295505542 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + Inventory | 14 | 12713 | 0.010598364413851882 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + Overheated + Inventory | 15 | 11525 | 0.009607972144233694 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Overheated + InCombat + ShieldBroken + Inventory | 15 | 11191 | 0.009329528526344406 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + Overheated + InCombat + ShieldBroken + Inventory | 17 | 10281 | 0.008570894717125086 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + InCombat + ShieldBroken + Inventory | 16 | 8745 | 0.007290387540245002 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Overheated + InCombat + Boosting + Inventory | 16 | 7319 | 0.00610158335129253 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + Overheated + InCombat + Inventory | 15 | 7011 | 0.005844814985095222 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + InCombat + Boosting + Inventory | 16 | 3105 | 0.0025885252501384485 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + ShieldBroken + Inventory | 13 | 2941 | 0.0024518044317736482 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + Boosting + Inventory | 15 | 2731 | 0.0022767350911845744 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + InCombat + Boosting + ShieldBroken + Inventory | 17 | 2008 | 0.0016739963614421916 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Overheated + InCombat + Boosting + ShieldBroken + Inventory | 17 | 1856 | 0.0015472795053967667 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + InCombat + Boosting + ShieldBroken + Inventory | 17 | 1653 | 0.0013780458094939953 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + Overheated + InCombat + ShieldBroken + Inventory | 16 | 1607 | 0.001339697287269722 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + InCombat + Boosting + ShieldBroken + Inventory | 16 | 1421 | 0.0011846358713193995 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + Overheated + InCombat + Boosting + Inventory | 17 | 1289 | 0.0010745922858062674 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Overheated + InCombat + Boosting + Inventory | 15 | 1263 | 0.001052917034114287 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + InCombat + Boosting + Inventory | 15 | 1237 | 0.0010312417824223062 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + Overheated + ShieldBroken + Inventory | 16 | 1211 | 0.0010095665307303257 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Overheated + Inventory | 13 | 813 | 0.0006777684471377001 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + ShieldBroken + Inventory | 15 | 745 | 0.0006210793273279048 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Boosting + ShieldBroken + Inventory | 14 | 709 | 0.0005910674403697778 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + Overheated + InCombat + Boosting + Inventory | 17 | 542 | 0.00045184563142513335 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Overheated + InCombat + Boosting + ShieldBroken + Inventory | 16 | 424 | 0.00035347333528460616 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + ShieldBroken + Inventory | 14 | 402 | 0.00033513273769908414 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + Overheated + Inventory | 15 | 267 | 0.00022258816160610815 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + Overheated + Inventory | 14 | 251 | 0.00020924954518027395 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Overheated + Boosting + Inventory | 15 | 211 | 0.00017590300411568846 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + Boosting + ShieldBroken + Inventory | 16 | 195 | 0.00016256438768985426 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + Overheated + InCombat + Boosting + Inventory | 16 | 141 | 0.00011754655725266384 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Overheated + ShieldBroken + Inventory | 15 | 118 | 9.83722961405272E-05 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Boosting + ShieldBroken + Inventory | 15 | 109 | 9.086932440099546E-05 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + Overheated + InCombat + Boosting + ShieldBroken + Inventory | 18 | 105 | 8.753467029453691E-05 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + Overheated + InCombat + Boosting + ShieldBroken + Inventory | 18 | 86 | 7.16950632888588E-05 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Overheated + ShieldBroken + Inventory | 14 | 75 | 6.252476449609779E-05 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + ShieldBroken + Inventory | 15 | 63 | 5.252080217672215E-05 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + Overheated + InCombat + Boosting + ShieldBroken + Inventory | 17 | 63 | 5.252080217672215E-05 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + ShieldBroken + Inventory | 14 | 39 | 3.2512877537970854E-05 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + Boosting + Inventory | 15 | 28 | 2.334257874520984E-05 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + Boosting + Inventory | 14 | 13 | 1.0837625845990285E-05 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + Overheated + ShieldBroken + Inventory | 16 | 4 | 3.334654106458549E-06 |

### Checkpoint phases

| Tick | Checksum | NativeComponentCountsVerified |
| --- | --- | --- |
| 3600 | 00B3EE0358C05B75 | true |
| 7200 | 725152DEBE2D71BE | true |
| 10800 | 0A298A1A33B3562A | true |
| 14400 | 5F6CCB51E201C076 | true |
| 18000 | 6EA5A47C2239A9D4 | true |
| 21600 | C1B9E32CC66D6245 | true |
| 25200 | 4E7924A36CF1FC8D | true |
| 28800 | 646CFEBF02AD4578 | true |
| 32400 | CC613F8B6C207D6F | true |
| 36000 | 19F75F83D5B54706 | true |
| 39600 | 1CAA401CC2007923 | true |
| 43200 | 4C52CCE86B831955 | true |
| 46800 | A741AC2743C5DB46 | true |
| 50400 | BF63CD3A20B87938 | true |
| 54000 | B33915A4E39EE58D | true |
| 57600 | 8FC449EA94F69BA6 | true |
| 61200 | 818B64DE926857B8 | true |
| 64800 | FF2684656B69B3D7 | true |
| 68400 | 9D25A1BB7D2C2711 | true |
| 72000 | 9A6B99F809E62022 | true |
| 75600 | 7126D89B7BE1B6E4 | true |
| 79200 | 892A14AB3DAD68DE | true |
| 82800 | D8D66E02BF5D4EDF | true |
| 86400 | 83C484AF2D627955 | true |
| 90000 | 86E74C995FDFF13A | true |
| 93600 | AA695BD9767F73F9 | true |
| 97200 | 1DD10C677B587E2C | true |
| 100800 | 6355DB5EFE84F590 | true |
| 104400 | 78DBF8B7160D125D | true |
| 108000 | 374773A7D7A76049 | true |

### Recorded configuration

| Field | Value |
| --- | --- |
| Seed | 7331 |
| ShipsPerTeam | 96 |
| CarriersPerTeam | 3 |
| ArenaHalfSize | 210 |
| ChurnMultiplier | 1 |
| TickRate | 60 |
| ReplenishCarriers | true |
| ParticleMultiplier | 1 |
| MaxProjectiles | 24000 |
| MaxDebris | 16000 |
| MaxParticles | 32000 |

### Recorded provenance

| Field | Value |
| --- | --- |
| SchemaVersion | 1 |
| SimulatedSeconds | 1800 |
| SourceResult | results/nominal-secs.json |
| SourceSha256 | 3CBE8776024CFD9DB958BC3E3E353BDDFDBA2033142D4414DEE37D46A628A1B1 |
| TickCsvSha256 | B45B86CEAE5E5B236942F8CE7E7E1AC3FB92023F7998FC9A4420720D370CC770 |
| ObserverSha256 | D074B983423F90D4E976B5E2A5FA6003F1CEF0EC2D37E778BF0F8C255A26EE79 |
| BinaryHashes.SpaceBattle.Core.dll | C33F0C46BD5725E342233BF456E2EACF412FDDAD2EF91C43827E513D3E9D4120 |
| BinaryHashes.SpaceBattle.SECS.dll | 5896308C178CE8FD87680540B70CAB72CEFF4D37EECC704C761B1FC17990E0D8 |
| BinaryHashes.SECS.dll | 3848BFF3E9D65815AEC89B2B3E746215E5AF94CDFBA82F87CD5D40C8A4358AFF |
| BinaryHashes.Aether.Physics2D.dll | A35C71A9BACA513C0B50BBE29EBCED17719DE0349115C4C32A88F6F09AE0C121 |

## high

Ticks: 108000; checksum: `D359225CC9541DE1`.

Separate diagnostic replay, every end-of-Step tick 1..108000; setup excluded from sums and API operations; no performance measurement

### Census summary

| Field | Value |
| --- | --- |
| EntityTicks | 149014611 |
| ComponentTicks | 513334356 |
| MeanEntities | 1379.7649166666667 |
| MeanComponents | 4753.095888888889 |
| MeanComponentsPerEntity | 3.44485921585233 |
| MeanOfPerTickMeans | 3.751266631657848 |
| MinEntities | 199 |
| MaxEntities | 10292 |
| MinComponents | 1291 |
| MaxComponents | 32508 |
| ObservedCompositions | 70 |

### Components and API activity

| Name | LogicalPayloadBytes | Initial | Final | MeanActive | MinActive | MaxActive | EntityTicks | EntityPresencePercent | AddsDuringSimulation | RemovesDuringSimulation | GetCalls | HasCalls | QueryBindings |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Identity | 12 | 198 | 75 | 55.36303703703704 | 22 | 198 | 5979208 | 4.01249780801696 | 3473 | 3596 | 7651393 | 11275497 | 40925764 |
| Transform | 20 | 198 | 2513 | 1379.7649166666667 | 199 | 10292 | 149014611 | 100 | 4412797 | 4410482 | 87723424 | 0 | 167877430 |
| Motion | 32 | 198 | 2513 | 1379.7649166666667 | 199 | 10292 | 149014611 | 100 | 4412797 | 4410482 | 12688779 | 0 | 149524706 |
| Hull | 12 | 198 | 75 | 55.36303703703704 | 22 | 198 | 5979208 | 4.01249780801696 | 3473 | 3596 | 24061027 | 0 | 6762276 |
| Shield | 20 | 198 | 75 | 55.36303703703704 | 22 | 198 | 5979208 | 4.01249780801696 | 3473 | 3596 | 16944237 | 0 | 5979331 |
| Heat | 12 | 198 | 75 | 55.36303703703704 | 22 | 198 | 5979208 | 4.01249780801696 | 3473 | 3596 | 10997405 | 0 | 5979331 |
| Brain | 28 | 198 | 75 | 55.36303703703704 | 22 | 198 | 5979208 | 4.01249780801696 | 3473 | 3596 | 188674 | 0 | 21790095 |
| Weapon | 32 | 198 | 75 | 55.36303703703704 | 22 | 198 | 5979208 | 4.01249780801696 | 3473 | 3596 | 2113531 | 0 | 5979331 |
| SecondaryWeapon | 32 | 102 | 45 | 35.66708333333333 | 15 | 102 | 3852045 | 2.5850116133913876 | 1654 | 1711 | 0 | 3596 | 3852102 |
| Collider | 12 | 198 | 75 | 55.36303703703704 | 22 | 198 | 5979208 | 4.01249780801696 | 3473 | 3596 | 429125 | 0 | 0 |
| PhysicsLink | 4 | 198 | 75 | 55.36303703703704 | 22 | 198 | 5979208 | 4.01249780801696 | 3473 | 3596 | 11179313 | 0 | 0 |
| CombatState | 4 | 198 | 75 | 55.36303703703704 | 22 | 198 | 5979208 | 4.01249780801696 | 3473 | 3596 | 959716 | 0 | 0 |
| Carrier | 20 | 6 | 4 | 3.841222222222222 | 2 | 6 | 414852 | 0.2783968613654939 | 128 | 130 | 18336 | 3596 | 414854 |
| Docked | 12 | 0 | 1 | 7.2495 | 0 | 20 | 782946 | 0.5254155916294678 | 3250 | 3249 | 117667 | 27761754 | 782945 |
| Overheated | 4 | 0 | 11 | 13.487583333333333 | 0 | 40 | 1456659 | 0.9775276331795411 | 5880 | 5869 | 0 | 9562116 | 0 |
| InCombat | 4 | 0 | 65 | 37.13961111111111 | 0 | 166 | 4011078 | 2.691734705129016 | 8279 | 8214 | 4011013 | 6947261 | 0 |
| Boosting | 4 | 0 | 26 | 11.763212962962964 | 0 | 77 | 1270427 | 0.8525519688804207 | 10859 | 10833 | 1270401 | 11200298 | 0 |
| ShieldBroken | 4 | 0 | 22 | 6.385592592592593 | 0 | 75 | 689644 | 0.46280293950503953 | 9493 | 9471 | 0 | 6453628 | 0 |
| Projectile | 36 | 0 | 229 | 125.28775 | 0 | 841 | 13531077 | 9.080369306872868 | 512410 | 512181 | 14043258 | 0 | 41105183 |
| Debris | 16 | 0 | 213 | 504.7307962962963 | 7 | 1656 | 54510926 | 36.58092695353209 | 482995 | 482782 | 54902933 | 0 | 163532352 |
| Particle | 32 | 0 | 1996 | 694.3833333333333 | 0 | 8617 | 74993400 | 50.32620593157808 | 3413919 | 3411923 | 78297410 | 0 | 224976208 |
| Lifetime | 4 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| Inventory | 136 | 198 | 75 | 55.36303703703704 | 22 | 198 | 5979208 | 4.01249780801696 | 3473 | 3596 | 3224 | 0 | 5979331 |

### Entity groups

| Name | EntityTicks | ComponentTicks | MeanEntities | MeanComponentsPerEntity | MinEntities | MaxEntities | EntitySharePercent |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Escorts | 5564356 | 77653350 | 51.52181481481482 | 13.95549637729865 | 20 | 192 | 3.7341009466514663 |
| Carriers | 414852 | 6574797 | 3.841222222222222 | 15.848536345491887 | 2 | 6 | 0.2783968613654939 |
| Projectiles | 13531077 | 40593231 | 125.28775 | 3 | 0 | 841 | 9.080369306872868 |
| Debris | 54510926 | 163532778 | 504.7307962962963 | 3 | 7 | 1656 | 36.58092695353209 |
| Particles | 74993400 | 224980200 | 694.3833333333333 | 3 | 0 | 8617 | 50.32620593157808 |
| Unclassified | 0 | 0 | 0 | n/a | 0 | 0 | 0 |

### Component-count histogram

| Components | EntityTicks | Percent |
| --- | --- | --- |
| 3 | 143035403 | 95.98750219198304 |
| 12 | 531186 | 0.3564657159692884 |
| 13 | 1527503 | 1.0250692799513466 |
| 14 | 1716411 | 1.1518407413082465 |
| 15 | 1431945 | 0.9609426823252922 |
| 16 | 642878 | 0.43141943980244996 |
| 17 | 125731 | 0.08437494763516848 |
| 18 | 3554 | 0.002385001025167928 |

### All observed signatures

| Components | ComponentCount | EntityTicks | Percent |
| --- | --- | --- | --- |
| Transform + Motion + Particle | 3 | 74993400 | 50.32620593157808 |
| Transform + Motion + Debris | 3 | 54510926 | 36.58092695353209 |
| Transform + Motion + Projectile | 3 | 13531077 | 9.080369306872868 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + InCombat + Inventory | 14 | 701503 | 0.4707612195155816 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Inventory | 13 | 662771 | 0.4447691374371336 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + InCombat + Inventory | 13 | 568401 | 0.38143977706991433 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Overheated + InCombat + Inventory | 15 | 546856 | 0.36698146331435916 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Inventory | 12 | 531186 | 0.3564657159692884 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Boosting + Inventory | 14 | 282586 | 0.18963643773159936 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + InCombat + Inventory | 15 | 280693 | 0.18836609250350625 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + InCombat + Boosting + Inventory | 14 | 259154 | 0.1739118051987533 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + InCombat + Boosting + Inventory | 15 | 257775 | 0.17298639258938173 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Boosting + Inventory | 13 | 222604 | 0.14938400906203755 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + Overheated + InCombat + Inventory | 16 | 203530 | 0.1365839219618538 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Overheated + InCombat + ShieldBroken + Inventory | 16 | 184121 | 0.12355902469188072 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + InCombat + Inventory | 14 | 116535 | 0.07820374070566811 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + InCombat + ShieldBroken + Inventory | 15 | 111871 | 0.07507384628209378 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + Inventory | 14 | 105819 | 0.07101249957294456 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Overheated + InCombat + Inventory | 14 | 104475 | 0.07011057459325247 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + InCombat + ShieldBroken + Inventory | 14 | 95600 | 0.06415478278166964 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + Overheated + InCombat + Inventory | 16 | 94489 | 0.06340921830812953 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + Overheated + InCombat + ShieldBroken + Inventory | 17 | 65359 | 0.04386079966346387 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + Inventory | 13 | 64532 | 0.04330582052789441 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + InCombat + Inventory | 15 | 61787 | 0.04146371928588936 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + InCombat + Boosting + ShieldBroken + Inventory | 16 | 46289 | 0.03106339686381492 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Overheated + InCombat + Boosting + Inventory | 16 | 44484 | 0.029852106247487367 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + InCombat + Boosting + ShieldBroken + Inventory | 15 | 43884 | 0.029449461167267686 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Overheated + InCombat + ShieldBroken + Inventory | 15 | 42214 | 0.028328765693989566 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Overheated + Inventory | 14 | 28143 | 0.018886067487704276 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + Overheated + InCombat + ShieldBroken + Inventory | 17 | 25466 | 0.017089599354790787 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + InCombat + Boosting + Inventory | 16 | 24919 | 0.016722521256657175 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + Overheated + InCombat + Inventory | 15 | 23452 | 0.015738054035520047 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + Overheated + Inventory | 15 | 21154 | 0.014195923378278658 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + Inventory | 14 | 20230 | 0.013575849954740345 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Overheated + InCombat + Boosting + ShieldBroken + Inventory | 17 | 19312 | 0.012959802982004227 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + InCombat + ShieldBroken + Inventory | 16 | 16859 | 0.011313655679039419 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + InCombat + ShieldBroken + Inventory | 15 | 11566 | 0.007761654996368108 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + Boosting + Inventory | 15 | 10014 | 0.0067201463888665255 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + InCombat + Boosting + Inventory | 16 | 9734 | 0.006532245351430673 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Overheated + InCombat + Boosting + Inventory | 15 | 9719 | 0.006522179224425181 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Overheated + Inventory | 13 | 8327 | 0.005588042638315514 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + Overheated + InCombat + Boosting + Inventory | 17 | 6590 | 0.004422385131079529 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + InCombat + Boosting + Inventory | 15 | 6328 | 0.004246563446050267 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + Overheated + InCombat + ShieldBroken + Inventory | 16 | 5610 | 0.003764731500054045 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Overheated + InCombat + Boosting + ShieldBroken + Inventory | 16 | 4931 | 0.0033090714842721027 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + InCombat + Boosting + ShieldBroken + Inventory | 17 | 4494 | 0.003015811650845433 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + Overheated + InCombat + Boosting + Inventory | 17 | 3865 | 0.0025937053917484644 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + InCombat + Boosting + ShieldBroken + Inventory | 16 | 3106 | 0.002084359365270564 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Overheated + Boosting + Inventory | 15 | 2959 | 0.0019857113206167413 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + Overheated + ShieldBroken + Inventory | 16 | 2888 | 0.0019380649861240789 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + Overheated + InCombat + Boosting + ShieldBroken + Inventory | 18 | 2799 | 0.0018783392992248256 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + Overheated + InCombat + Boosting + Inventory | 16 | 1721 | 0.0011549203050967936 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + Overheated + Inventory | 15 | 1055 | 0.0007079842660529443 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + ShieldBroken + Inventory | 13 | 868 | 0.0005824932160511428 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Overheated + Boosting + Inventory | 14 | 770 | 0.0005167278529485944 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + Overheated + InCombat + Boosting + ShieldBroken + Inventory | 18 | 755 | 0.0005066617259431023 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + Overheated + Inventory | 14 | 739 | 0.0004959245238039107 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + Overheated + InCombat + Boosting + ShieldBroken + Inventory | 17 | 640 | 0.0004294880855676629 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Boosting + ShieldBroken + Inventory | 14 | 325 | 0.0002180994184523288 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + Boosting + Inventory | 15 | 305 | 0.00020467791577833934 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + Boosting + Inventory | 14 | 247 | 0.0001657555580237699 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + InCombat + ShieldBroken + Inventory | 16 | 187 | 0.0001254910500018015 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + ShieldBroken + Inventory | 14 | 159 | 0.00010670094625821625 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + ShieldBroken + Inventory | 15 | 159 | 0.00010670094625821625 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + Overheated + Boosting + Inventory | 15 | 103 | 6.912073877104575E-05 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Overheated + ShieldBroken + Inventory | 14 | 72 | 4.831740962636208E-05 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + Collider + PhysicsLink + CombatState + Docked + ShieldBroken + Inventory | 14 | 54 | 3.623805721977156E-05 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Overheated + ShieldBroken + Inventory | 15 | 51 | 3.4224831818673135E-05 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Docked + Overheated + Boosting + Inventory | 16 | 10 | 6.7107513369947325E-06 |
| Identity + Transform + Motion + Hull + Shield + Heat + Brain + Weapon + SecondaryWeapon + Collider + PhysicsLink + CombatState + Carrier + InCombat + Boosting + ShieldBroken + Inventory | 17 | 5 | 3.3553756684973662E-06 |

### Checkpoint phases

| Tick | Checksum | NativeComponentCountsVerified |
| --- | --- | --- |
| 3600 | 315E7116DDCD7FD7 | true |
| 7200 | D25FB357F59CB3D8 | true |
| 10800 | 13A34B9B9E66DE73 | true |
| 14400 | EAA9BD76D621C8C4 | true |
| 18000 | A9D6F4822D99F1FB | true |
| 21600 | 4044ACBC73AC6735 | true |
| 25200 | 14D263396EB302E6 | true |
| 28800 | 1F1AB87D8313DD41 | true |
| 32400 | C456DD37B151C775 | true |
| 36000 | 7423E9B8723148BE | true |
| 39600 | 3A9B9B0983FF0163 | true |
| 43200 | 5CBFE617139A2C8E | true |
| 46800 | 10FE70D24B8ADCFE | true |
| 50400 | FACDD85A737DE100 | true |
| 54000 | 6838C9DB8DA2E54C | true |
| 57600 | A81FF4528AC1EF0F | true |
| 61200 | B6B8FF02B2AFCD1D | true |
| 64800 | F8328FBF2F271386 | true |
| 68400 | 62FAA242A2141914 | true |
| 72000 | 8CA625F19C0A8B66 | true |
| 75600 | 3CC3F2E10B889EFB | true |
| 79200 | CFC9D3B4A8A3614B | true |
| 82800 | 7EBB60B77F49DA46 | true |
| 86400 | DCA9081EAD1926FC | true |
| 90000 | 7C10EB465C72C56E | true |
| 93600 | 83A8D251895C2DD5 | true |
| 97200 | CE59D12ECE08ABF9 | true |
| 100800 | 158CC480959CE5B8 | true |
| 104400 | FF6675D41B692A74 | true |
| 108000 | D359225CC9541DE1 | true |

### Recorded configuration

| Field | Value |
| --- | --- |
| Seed | 7331 |
| ShipsPerTeam | 96 |
| CarriersPerTeam | 3 |
| ArenaHalfSize | 210 |
| ChurnMultiplier | 2 |
| TickRate | 60 |
| ReplenishCarriers | true |
| ParticleMultiplier | 1 |
| MaxProjectiles | 24000 |
| MaxDebris | 16000 |
| MaxParticles | 32000 |

### Recorded provenance

| Field | Value |
| --- | --- |
| SchemaVersion | 1 |
| SimulatedSeconds | 1800 |
| SourceResult | results/high-secs.json |
| SourceSha256 | 1F0123188D14686B7A2A6EF59D91627DAE4CD99B523F67724CB86F014F101BBD |
| TickCsvSha256 | 1A3184A259E8C01F12B09B1E39AFA9D045255B7FB569E0D9B539D0908B11FC61 |
| ObserverSha256 | D074B983423F90D4E976B5E2A5FA6003F1CEF0EC2D37E778BF0F8C255A26EE79 |
| BinaryHashes.SpaceBattle.Core.dll | C33F0C46BD5725E342233BF456E2EACF412FDDAD2EF91C43827E513D3E9D4120 |
| BinaryHashes.SpaceBattle.SECS.dll | 5896308C178CE8FD87680540B70CAB72CEFF4D37EECC704C761B1FC17990E0D8 |
| BinaryHashes.SECS.dll | 3848BFF3E9D65815AEC89B2B3E746215E5AF94CDFBA82F87CD5D40C8A4358AFF |
| BinaryHashes.Aether.Physics2D.dll | A35C71A9BACA513C0B50BBE29EBCED17719DE0349115C4C32A88F6F09AE0C121 |

## Reproduction

Local input directory: `artifacts/benchmarks/history/docs/space-battle/composition`.

```sh
dotnet run --project tools/BenchmarkTools -- report composition --data "artifacts/benchmarks/history/docs/space-battle/composition" --output "docs/space-battle/composition"
```
