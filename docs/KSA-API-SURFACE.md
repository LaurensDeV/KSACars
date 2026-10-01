# KSA API surface

Every external type and member `KSACars.dll` binds to, read out of its
metadata tables by `tools/api-surface.sh`. **Generated - do not edit.**

This is the checklist for a KSA update: anything here that changed shape in the new
build is a breaking change for this mod, and anything not here cannot be. See the
`upgrade-ksa` skill, which diffs the decompiled sources against exactly this list.

148 types and 337 members across 6 assemblies.

## BepuUtilities

### BepuUtilities.Symmetric3x3

- `BepuUtilities.Symmetric3x3 Invert(BepuUtilities.Symmetric3x3)`
- `float XX`
- `float YX`
- `float YY`
- `float ZX`
- `float ZY`
- `float ZZ`

## Brutal.Core.Numerics

### Brutal.Numerics.Pack

*referenced as a type only*

### Brutal.Numerics.Pack+Float

*referenced as a type only*

### Brutal.Numerics.Unpack

*referenced as a type only*

### Brutal.Numerics.Unpack+Float

*referenced as a type only*

### Brutal.Numerics.byte4

- `void .ctor(byte, byte, byte, byte)`

### Brutal.Numerics.double3

- `Brutal.Numerics.double3 Cross(Brutal.Numerics.double3, Brutal.Numerics.double3)`
- `Brutal.Numerics.double3 Unpack(ref Brutal.Numerics.float3, Float)`
- `Brutal.Numerics.double3 get_Zero()`
- `Brutal.Numerics.double3 op_Addition(Brutal.Numerics.double3, Brutal.Numerics.double3)`
- `Brutal.Numerics.double3 op_Division(Brutal.Numerics.double3, double)`
- `Brutal.Numerics.double3 op_Multiply(Brutal.Numerics.double3, double)`
- `Brutal.Numerics.double3 op_Subtraction(Brutal.Numerics.double3, Brutal.Numerics.double3)`
- `Brutal.Numerics.double3 op_UnaryNegation(Brutal.Numerics.double3)`
- `bool Equals(Brutal.Numerics.double3)`
- `double Dot(Brutal.Numerics.double3, Brutal.Numerics.double3)`
- `double Length()`
- `double LengthSquared()`
- `double X`
- `double Y`
- `double Z`
- `void .ctor(double, double, double)`

### Brutal.Numerics.double4

- `double W`
- `double X`
- `double Y`

### Brutal.Numerics.double4x4

- `double get_M11()`
- `double get_M12()`
- `double get_M13()`
- `double get_M21()`
- `double get_M22()`
- `double get_M23()`
- `double get_M31()`
- `double get_M32()`
- `double get_M33()`

### Brutal.Numerics.doubleQuat

- `Brutal.Numerics.double3 op_Multiply(Brutal.Numerics.doubleQuat, Brutal.Numerics.double3)`
- `Brutal.Numerics.doubleQuat CreateFromAxisAngle(Brutal.Numerics.double3, double)`
- `Brutal.Numerics.doubleQuat get_Identity()`
- `Brutal.Numerics.doubleQuat op_Multiply(Brutal.Numerics.doubleQuat, Brutal.Numerics.doubleQuat)`
- `double W`
- `double X`
- `double Y`
- `double Z`

### Brutal.Numerics.float2

- `float X`
- `float Y`
- `void .ctor(float, float)`

### Brutal.Numerics.float3

- `Brutal.Numerics.float3 Pack(ref Brutal.Numerics.double3, Float)`
- `Brutal.Numerics.float3 get_Zero()`
- `Brutal.Numerics.float3 op_UnaryNegation(Brutal.Numerics.float3)`
- `void .ctor(float, float, float)`

### Brutal.Numerics.float4

- `void .ctor(float, float, float, float)`

### Brutal.Numerics.float4x4

- `Brutal.Numerics.float3 get_Translation()`
- `Brutal.Numerics.float4x4 CreateFromQuaternion(Brutal.Numerics.floatQuat)`
- `Brutal.Numerics.float4x4 CreateTranslation(Brutal.Numerics.float3)`
- `Brutal.Numerics.float4x4 op_Multiply(Brutal.Numerics.float4x4, Brutal.Numerics.float4x4)`

### Brutal.Numerics.floatQuat

- `void .ctor(float, float, float, float)`

### Brutal.Numerics.int2

- `int X`
- `int Y`

## Brutal.Glfw

### Brutal.GlfwApi.GlfwKey

*referenced as a type only*

### Brutal.GlfwApi.GlfwKeyAction

*referenced as a type only*

### Brutal.GlfwApi.GlfwModifier

*referenced as a type only*

### Brutal.GlfwApi.GlfwWindow

*referenced as a type only*

## Brutal.ImGui

### Brutal.ImGuiApi.ImGui

- `Brutal.ImGuiApi.ImGuiIOPtr GetIO()`
- `Brutal.ImGuiApi.ImGuiViewportPtr GetMainViewport()`
- `Brutal.Numerics.float2 GetMousePos()`
- `bool Begin(Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImGuiWindowFlags)`
- `bool Button(Brutal.ImGuiApi.ImString, ref System.Nullable`1<Brutal.Numerics.float2>)`
- `bool Checkbox(Brutal.ImGuiApi.ImString, ref bool)`
- `bool IsItemActive()`
- `bool IsItemDeactivatedAfterEdit()`
- `bool IsMouseClicked(Brutal.ImGuiApi.ImGuiMouseButton, bool)`
- `bool IsMouseDown(Brutal.ImGuiApi.ImGuiMouseButton)`
- `bool RadioButton(Brutal.ImGuiApi.ImString, bool)`
- `bool SliderFloat(Brutal.ImGuiApi.ImString, ref float, float, float, Brutal.ImGuiApi.ImString, Brutal.ImGuiApi.ImGuiSliderFlags)`
- `void End()`
- `void SameLine(float, float)`
- `void SetNextWindowPos(ref Brutal.Numerics.float2, Brutal.ImGuiApi.ImGuiCond, ref System.Nullable`1<Brutal.Numerics.float2>)`
- `void Text(Brutal.ImGuiApi.ImString)`
- `void TextDisabled(Brutal.ImGuiApi.ImString)`

### Brutal.ImGuiApi.ImGuiCond

*referenced as a type only*

### Brutal.ImGuiApi.ImGuiIOPtr

- `ref bool get_KeyShift()`
- `ref bool get_WantCaptureMouse()`

### Brutal.ImGuiApi.ImGuiMouseButton

*referenced as a type only*

### Brutal.ImGuiApi.ImGuiSliderFlags

*referenced as a type only*

### Brutal.ImGuiApi.ImGuiViewportPtr

- `ref Brutal.Numerics.float2 get_WorkPos()`
- `ref Brutal.Numerics.float2 get_WorkSize()`

### Brutal.ImGuiApi.ImGuiWindowFlags

*referenced as a type only*

### Brutal.ImGuiApi.ImString

- `Brutal.ImGuiApi.ImString op_Implicit(string)`
- `void .ctor(int, int)`
- `void AppendFormatted(string, int, string)`
- `void AppendFormatted<1>(!!0, int, string)`
- `void AppendLiteral(System.ReadOnlySpan`1<char>)`

## KSA

### KSA.AnimatedRenderable

- `System.Collections.Generic.List`1<KSA.IAnimProcessor> AnimProcessors`

### KSA.Astronomical

- `Brutal.Numerics.double3 GetPositionEcl()`
- `Brutal.Numerics.double3 GetVelocityEcl()`
- `KSA.AtmosphereReference GetAtmosphereReference()`
- `KSA.OrbitView OrbitView`
- `KSA.Rendering.Water.Data.OceanReference GetOceanReference()`
- `double get_MaxTerrainRadius()`
- `double get_MeanRadius()`
- `string get_Id()`
- `void UpdatePerFrameData()`

### KSA.AstronomicalData

- `string get_Id()`

### KSA.AtmosphereReference

- `KSA.PhysicalAtmosphereReference Physical`

### KSA.BubbleClutterStatics

- `float DisplaceEnergyPerKg`

### KSA.BubbleOrigin

*referenced as a type only*

### KSA.Camera

- `Brutal.Numerics.double3 EgoToEcl(Brutal.Numerics.double3)`
- `Brutal.Numerics.double3 GetPositionEgo(KSA.IPosition)`
- `Brutal.Numerics.double3 GetRightEcl()`
- `Brutal.Numerics.double3 GetVelocityEgo(KSA.IVelocity)`
- `Brutal.Numerics.double4 EgoToClipDouble(Brutal.Numerics.double3)`
- `Brutal.Numerics.float2 EclToScreen(Brutal.Numerics.double3, bool)`
- `Brutal.Numerics.int2 FramebufferSize`
- `KSA.IFollowable get_Following()`
- `KSA.Ray ScreenToEgoRay(Brutal.Numerics.float2)`
- `float GetFieldOfView()`
- `void SetFollow(KSA.IFollowable, bool, bool, bool)`

### KSA.Celestial

- `Brutal.Numerics.double3 GetDirCcfFromLatLon(double, double)`
- `Brutal.Numerics.double3 GetSurfacePositionEclFromCce(Brutal.Numerics.double3, bool)`
- `Brutal.Numerics.doubleQuat GetCcf2Cce()`
- `double GetLatitudeFromCce(Brutal.Numerics.double3)`
- `double GetLongitudeFromCce(Brutal.Numerics.double3)`
- `double GetTerrainHeightFromDirCce(Brutal.Numerics.double3, bool)`
- `double GetTerrainHeightFromDirCcf(Brutal.Numerics.double3, bool)`
- `double get_MaxTerrainHeightApprox()`

### KSA.CelestialSystem

- `KSA.Astronomical GetIndex(int)`
- `KSA.LookupCollection`1<KSA.Astronomical> get_All()`
- `int get_Count()`

### KSA.CharacterAvatar

- `CharacterCore Core`

### KSA.CharacterAvatar+CharacterCore

- `KSA.AnimatedRenderable CharacterModel`

### KSA.ClutterEcotypePhysicalData

- `KSA.ClutterEcotypeReference EcotypeReference`

### KSA.ClutterEcotypeReference

- `System.Collections.Generic.List`1<KSA.ClutterObjectTemplate> ClutterObjects`

### KSA.ClutterObjectTemplate

- `double MassKg`

### KSA.ColliderModule

- `Brutal.Numerics.double3 PositionPartAsmb`
- `bool NeedsColliderUpdate`

### KSA.Constants

- `string get_DocumentsFolderPath()`

### KSA.ConstraintSim

- `KSA.ShapesUnlock UnlockShapesBlocking()`

### KSA.CrewAssignmentWindow

- `void FillSeats(System.Collections.Generic.List`1<KSA.IVASeat>, KSA.PartTree, string, bool)`

### KSA.DefaultVehicleSaves

- `KSA.VehicleSave FindSave(string)`

### KSA.DensityReference

- `double op_Implicit(KSA.DensityReference)`

### KSA.DistanceReference

- `double InMeters()`
- `double op_Implicit(KSA.DistanceReference)`

### KSA.Double3Ex

- `Brutal.Numerics.double3 Transform(Brutal.Numerics.double3, Brutal.Numerics.double4x4)`
- `Brutal.Numerics.double3 Transform(Brutal.Numerics.double3, Brutal.Numerics.doubleQuat)`

### KSA.EVADoor

- `bool PerformEvaForSeat(KSA.Vehicle, KSA.IVASeat)`

### KSA.EngineFlags

*referenced as a type only*

### KSA.ExhaustAxialFade

- `KSA.ExhaustAxialFade NoFadeOut`

### KSA.ExhaustBendTarget

*referenced as a type only*

### KSA.ExhaustDiamondFade

- `KSA.ExhaustDiamondFade None`

### KSA.ExhaustSubmission

*referenced as a type only*

### KSA.GameAudio

- `KSA.Camera GetAudioCamera()`
- `void PlaySound(KSA.SoundEvent, KSA.SpatialAudio, ref KSA.IChannel, KSA.IAudio, float, bool)`

### KSA.GameSave

- `string get_Id()`

### KSA.GameSaves

- `void LoadSaveGame(string)`
- `void MakeUncompressedSave(string)`

### KSA.GameSettings

- `KSA.GameSettings get_Current()`
- `SimulationSettings Simulation`
- `bool GetGroundClutterCollisions()`

### KSA.GameSettings+SimulationSettings

- `bool GroundClutterCollisions`

### KSA.GasConditions

- `float Pressure`
- `float Temperature`

### KSA.GasProperties

- `float Gamma`
- `float SpecificGasConstant`

### KSA.GaugeCanvas

- `System.Collections.Generic.List`1<KSA.GaugeVisibilityFlag> VisibleInContext`
- `bool IsContextVisible()`

### KSA.GaugeVisibilityFlag

*referenced as a type only*

### KSA.GizmosRenderer

- `void DrawLine(Brutal.Numerics.double3, Brutal.Numerics.double3, Brutal.Numerics.float4)`
- `void DrawSphere(Brutal.Numerics.double3, float, Brutal.Numerics.float4)`

### KSA.GroundClutterRenderer

- `System.Collections.Generic.Dictionary`2<KSA.KeyHash, KSA.ClutterEcotypePhysicalData[]> PlanetPhysicalData`

### KSA.IAnimProcessor

*referenced as a type only*

### KSA.IAudio

*referenced as a type only*

### KSA.IChannel

- `bool IsPlaying()`
- `void SetParameter(KSA.KeyHash, float)`
- `void SetPaused(bool)`
- `void SetSpatialAudio(KSA.SpatialAudio)`
- `void Stop(bool)`
- `void set_PitchMultiplier(float)`
- `void set_VolumeMultiplier(float)`

### KSA.IFollowable

- `KSA.OrbitView get_OrbitView()`

### KSA.IGameViewport

- `KSA.OrbitController get_OrbitController()`
- `float get_IvaAudio()`

### KSA.IOrbiter

*referenced as a type only*

### KSA.IParentBody

- `Brutal.Numerics.double3 GetAngularVelocityCce()`
- `Brutal.Numerics.doubleQuat GetCce2Cci()`
- `Brutal.Numerics.doubleQuat GetCci2Cce()`
- `System.Collections.Generic.List`1<KSA.IOrbiter> get_Children()`
- `double get_Mu()`

### KSA.IPosition

- `Brutal.Numerics.double3 GetPositionEcl()`

### KSA.IVASeat

- `Brutal.Numerics.double3 PositionAsmb`
- `KSA.KeyHash AssignedKittenHash`
- `KSA.KittenRenderable get_Renderable()`
- `void UpdateSeatedRenderable(KSA.IViewport, int, double, ref Brutal.Numerics.double4x4)`

### KSA.IVelocity

- `Brutal.Numerics.double3 GetVelocityEcl()`

### KSA.IViewport

- `Brutal.Numerics.float2 get_Position()`
- `KSA.Camera GetCamera()`
- `bool get_Visible()`
- `int get_Height()`
- `int get_Width()`

### KSA.Input

- `bool Contains(ref RenderCore.Input.GlfwKeyEvent, KSA.InputAction)`

### KSA.InputAction

*referenced as a type only*

### KSA.InputEvents

- `TypedBuffer`1<VehicleInputData> VehicleInputBuffer`

### KSA.InputEvents+TypedBuffer`1

*referenced as a type only*

### KSA.InputEvents+VehicleInputData

- `Brutal.GlfwApi.GlfwKeyAction KeyAction`
- `Brutal.GlfwApi.GlfwModifier Modifiers`
- `KSA.InputAction Action`
- `KSA.Vehicle Vehicle`

### KSA.KinematicStates

- `Brutal.Numerics.double3 AngularVelocityPhys`
- `Brutal.Numerics.double3 PositionPhys`
- `Brutal.Numerics.double3 VelocityPhys`
- `Brutal.Numerics.doubleQuat Body2Phys`

### KSA.KittenRenderable

*referenced as a type only*

### KSA.KittenRosterData

- `KSA.KittenRosterEntryData Find(KSA.KeyHash)`
- `System.Collections.Generic.List`1<KSA.KittenRosterEntryData> Kittens`

### KSA.KittenRosterEntryData

- `KSA.KeyHash NameHash`
- `string Name`

### KSA.LookupCollection`1

*referenced as a type only*

### KSA.ManualControlInputs

- `bool EngineOn`
- `bool Sprint`
- `float EngineThrottle`

### KSA.MassProperties

- `BepuUtilities.Symmetric3x3 Inertia`

### KSA.Mod

- `string get_Id()`

### KSA.ModLibrary

- `!!0 Get<1>(string)`

### KSA.ModuleBase

- `string TemplateId`

### KSA.ModuleList

- `System.Span`1<!!0> Get<1>()`

### KSA.Orbit

- `KSA.Orbit CreateFromStateCci(KSA.IParentBody, KSA.UniverseTime, Brutal.Numerics.double3, Brutal.Numerics.double3, Brutal.Numerics.byte4)`
- `ref KSA.StateVectors get_StateVectors()`

### KSA.OrbitController

- `double DistancePower`

### KSA.OrbitView

- `double Azimuth`
- `double DistancePower`
- `double Elevation`

### KSA.Part

- `Brutal.Numerics.double3 get_PositionVehicleAsmb()`
- `Brutal.Numerics.doubleQuat get_Asmb2VehicleAsmb()`
- `KSA.ModuleList SubtreeModules`
- `KSA.Part get_FullPart()`
- `KSA.PowerConsumer LightSwitch`
- `System.ReadOnlySpan`1<KSA.Part> get_SubParts()`
- `bool IsLightSwitchedOff()`
- `string get_Id()`
- `void ResetCachedPosMatrixValues()`
- `void set_Asmb2ParentAsmb(Brutal.Numerics.doubleQuat)`
- `void set_PositionParentAsmb(Brutal.Numerics.double3)`
- `void set_Scale(Brutal.Numerics.double3)`

### KSA.PartTree

- `KSA.ModuleList Modules`
- `KSA.Part get_Root()`
- `System.ReadOnlySpan`1<KSA.Part> get_Parts()`
- `void RecomputeAllDerivedData()`
- `void UpdateRenderData(ref Brutal.Numerics.double4x4, bool, KSA.IViewport, int)`

### KSA.PartTreeRenderData

*referenced as a type only*

### KSA.PhysicalAtmosphereReference

- `KSA.DensityReference SeaLevelDensity`
- `KSA.DistanceReference ScaleHeight`
- `KSA.DistanceReference get_Height()`
- `double GetAtmosphericDensityAtAltitude(double)`
- `double GetAtmosphericPressure(KSA.Camera)`

### KSA.PhysicsStates

- `ref KSA.BubbleOrigin Origin`
- `ref KSA.KinematicStates Kinematic`
- `void GetStatesCcf(ref KSA.BubbleOrigin, ref KSA.KinematicStates, ref Brutal.Numerics.double3, ref Brutal.Numerics.double3, ref Brutal.Numerics.doubleQuat)`
- `void GetStatesCci(ref KSA.BubbleOrigin, ref KSA.KinematicStates, ref Brutal.Numerics.double3, ref Brutal.Numerics.double3, ref Brutal.Numerics.doubleQuat)`

### KSA.PlanetRenderer

- `KSA.GroundClutterRenderer get_GroundClutterRenderer()`

### KSA.PlumeData

*referenced as a type only*

### KSA.PowerConsumer

- `bool LightIsActive`

### KSA.Program

- `KSA.Camera GetMainCamera()`
- `KSA.Camera GetRenderCamera()`
- `KSA.GizmosRenderer GizmosRenderer`
- `KSA.IGameViewport get_MainViewport()`
- `KSA.PlanetRenderer GetPlanetRenderer()`
- `KSA.Program get_Instance()`
- `KSA.Rendering.Lighting.ILightSystem LightSystem`
- `KSA.Vehicle get_ControlledVehicle()`
- `KSA.VehicleEditor Editor`
- `System.ReadOnlySpan`1<KSA.Vehicle> get_VehiclesInFrame()`
- `double GetPlayerDeltaTime()`
- `void set_ControlledVehicle(KSA.Vehicle)`

### KSA.QuaternionEx

- `Brutal.Numerics.doubleQuat Inverse(Brutal.Numerics.doubleQuat)`

### KSA.Ray

- `Brutal.Numerics.double3 Direction`

### KSA.Rendering.Lighting.ELightFlags

*referenced as a type only*

### KSA.Rendering.Lighting.ILightSystem

- `void CreateLightInstance(KSA.Rendering.Lighting.Light, KSA.IViewport)`

### KSA.Rendering.Lighting.Light

- `KSA.Rendering.Lighting.Light CreateSpotLight(Brutal.Numerics.double3, Brutal.Numerics.double3, float, float, float, Brutal.Numerics.float3, float, KSA.Rendering.Lighting.ELightFlags)`

### KSA.Rendering.Water.Data.OceanReference

- `KSA.DensityReference Density`
- `KSA.DistanceReference Level`

### KSA.RocketDesign

- `float SolveMachNumberFromAreaRatio(KSA.GasProperties, double)`

### KSA.RocketNozzle

- `KSA.PlumeData ComputePlumeData(ref KSA.GasProperties, ref KSA.GasConditions, ref KSA.GasConditions, float, float, float, float, float, float, float)`
- `float ComputeMinGasVisibilityDensity(KSA.VolumetricExhaustTemplate, double)`

### KSA.ScreenshotCapture

- `void Request(int, string)`

### KSA.ShapesUnlock

*referenced as a type only*

### KSA.SimSpeed

- `void .ctor(double)`

### KSA.SimStep

- `double get_DeltaTime()`

### KSA.Situation

*referenced as a type only*

### KSA.SituationEx

- `bool HasTerrainContact(KSA.Situation)`
- `bool IsOnRails(KSA.Situation)`

### KSA.SoundBehavior

- `void Play(KSA.SpatialAudio, float, ref KSA.IChannel, bool)`

### KSA.SoundEvent

- `string SoundId`
- `void .ctor()`

### KSA.SpatialAudio

- `double Distance()`
- `double get_AtmosphericPressure()`
- `void .ctor(Brutal.Numerics.double3, Brutal.Numerics.double3, double)`
- `void .ctor(KSA.Astronomical, Brutal.Numerics.double3)`

### KSA.StateVectors

- `Brutal.Numerics.double3 PositionCci`
- `Brutal.Numerics.double3 VelocityCci`

### KSA.StellarBody

*referenced as a type only*

### KSA.ThrusterMapFlags

*referenced as a type only*

### KSA.Transform3D

- `Brutal.Numerics.double3 get_PositionEcl()`

### KSA.Universe

- `KSA.CelestialSystem get_CurrentSystem()`
- `KSA.KittenRosterData get_KittenRoster()`
- `KSA.SimStep GetLastSimStep()`
- `KSA.UniverseTime GetElapsedTime()`
- `bool IsPaused()`
- `double GetElapsedSeconds()`
- `double GetSimulationSpeed()`
- `double get_SimulationSpeed()`
- `void DestroyVehicleFromEvent(KSA.Vehicle, KSA.VehicleDestructionEvent)`
- `void SetSimulationSpeed(KSA.SimSpeed)`

### KSA.UniverseTime

*referenced as a type only*

### KSA.Vehicle

- `Brutal.Numerics.double3 GetSurfaceVelocityCci()`
- `Brutal.Numerics.double3 PosAsmbToBody(Brutal.Numerics.double3)`
- `Brutal.Numerics.double3 get_CenterOfMassAsmb()`
- `Brutal.Numerics.double4x4 GetMatrixAsmb2Ego(KSA.Camera)`
- `Brutal.Numerics.doubleQuat get_Body2Cce()`
- `KSA.IParentBody get_Parent()`
- `KSA.Orbit get_Orbit()`
- `KSA.PartTree get_Parts()`
- `KSA.PhysicsStates GetPhysicsStatesMutable()`
- `KSA.Situation get_Situation()`
- `KSA.ThrusterMapFlags GetThrusterFlags()`
- `KSA.Vehicle CreateVehicle(KSA.CelestialSystem, Brutal.Numerics.doubleQuat, Brutal.Numerics.double3, KSA.IParentBody, string, KSA.Part, KSA.Orbit)`
- `System.ReadOnlySpan`1<KSA.IVASeat> get_Crew()`
- `bool AreAllEnginesInactive()`
- `bool GetSprintInput()`
- `bool OnKey(RenderCore.Input.GlfwKeyEvent)`
- `bool SetSeatCrew(KSA.IVASeat, KSA.KeyHash, string, bool)`
- `bool get_HasPhysicsBubble()`
- `bool get_IsDisposed()`
- `float get_TotalMass()`
- `int get_SeatCount()`
- `ref KSA.MassProperties get_TotalMassPropsBody()`
- `void AddVolumetricExhaustInstances(KSA.Camera, KSA.VolumetricExhaustRenderer, double)`
- `void PrepareWorker(KSA.SimStep)`
- `void ProcessInput(KSA.InputAction, Brutal.GlfwApi.GlfwKeyAction, Brutal.GlfwApi.GlfwModifier)`
- `void TakeOffRails()`
- `void TeleportToLocation(KSA.Celestial, double, double)`
- `void UpdateAfterPartTreeModification()`
- `void UpdateSeatedCrewRenderData(KSA.IViewport, int)`

### KSA.VehicleDestructionEvent

*referenced as a type only*

### KSA.VehicleEditor

*referenced as a type only*

### KSA.VehicleSave

- `KSA.PartTree Load(KSA.IViewport)`
- `KSA.VehicleSaveData get_VehicleSaveData()`

### KSA.VehicleSaveData

*referenced as a type only*

### KSA.VehicleSaves

- `System.ReadOnlySpan`1<KSA.VehicleSave> AsSpan()`
- `void Refresh()`

### KSA.ViewportEx

- `bool Is(KSA.IViewport, KSA.ViewportType)`

### KSA.ViewportRegistry

- `KSA.IGameViewport get_MainViewport()`
- `System.ReadOnlySpan`1<KSA.IGameViewport> get_GameViews()`

### KSA.ViewportType

*referenced as a type only*

### KSA.VolumetricExhaustInstance

- `KSA.PlumeData LastPlumeData`
- `KSA.VolumetricExhaustTemplate get_Template()`
- `bool get_IsLive()`
- `void .ctor(KSA.VolumetricExhaustReference)`
- `void UpdateState(double, bool, double, ref KSA.GasProperties, ref KSA.GasConditions, float, Brutal.Numerics.float3, Brutal.Numerics.float3, Brutal.Numerics.float3, Brutal.Numerics.float3, float, Brutal.Numerics.float3, float)`

### KSA.VolumetricExhaustReference

- `void .ctor()`
- `void Load()`
- `void set_Id(string)`

### KSA.VolumetricExhaustRenderer

- `KSA.ExhaustSubmission AddInstance(KSA.VolumetricExhaustInstance, ref KSA.ExhaustBendTarget, ref KSA.ExhaustAxialFade, ref KSA.ExhaustDiamondFade)`
- `bool get_Disabled()`

### KSA.VolumetricExhaustTemplate

*referenced as a type only*

## StarMap.API

### StarMap.API.StarMapAfterGuiAttribute

- `void .ctor()`

### StarMap.API.StarMapAfterOnFrameAttribute

- `void .ctor()`

### StarMap.API.StarMapAllModsLoadedAttribute

- `void .ctor()`

### StarMap.API.StarMapImmediateLoadAttribute

- `void .ctor()`

### StarMap.API.StarMapModAttribute

- `void .ctor()`

### StarMap.API.StarMapUnloadAttribute

- `void .ctor()`
