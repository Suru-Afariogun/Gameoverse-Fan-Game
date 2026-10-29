using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// City Level only: assigns building parallax depths by name so mixed "City Backgrounds"
/// folders don't force regular + grey buildings to move as one layer.
///
/// Depth (Background layerOrder — lower = closer / follows camera more):
///   regular "Background parts"              → 2
///   mid grey buildings                      → 4
///   light grey-blue (nearer far tier)       → 6  (second-to-last buildings)
///   light grey-blue (farther far tier)      → 8  (last buildings)
///   clouds                                  → 10
///
/// Important: the light-grey-blue prefab nests grey prefabs inside it. Only the outer
/// building root gets Background — nested grey children must not get their own mid-grey speed.
///
/// City Sky is left alone (no slow parallax) so it keeps covering the view.
/// </summary>
public static class CityBackgroundParallaxSetup
{
    private const int LayerRegularParts = 2;
    private const int LayerGrey = 4;
    private const int LayerLightBlueNear = 6;
    private const int LayerLightBlueFar = 8;
    private const int LayerClouds = 10;

    // Sprite draw order. Kit's rocket stays at Order 3 — keep buildings below it.
    // City Sky is Order 0; clouds must stay above 0 or the opaque sky covers them.
    // Player SortingGroup remains 4.
    private const int SortLightBlueFar = 0;
    private const int SortLightBlueNear = 0;
    private const int SortClouds = 1;
    private const int SortGrey = 1;
    private const int SortRegularParts = 2;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        // AfterSceneLoad RuntimeInitialize only runs for the *first* scene at play start.
        // Traveling HomeTown → City never re-ran setup, so buildings kept broken nesting /
        // never got their City layer assignment. Hook every scene load instead.
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyForCityScene(scene);
    }

    private static void ApplyForCityScene(Scene scene)
    {
        if (!scene.IsValid() || !IsCityScene(scene.name))
            return;

        // Must be DestroyImmediate: Object.Destroy waits until end of frame, so
        // ApplyParallaxLayer / Background.Awake would still see nested Backgrounds and
        // set drivesOwnTransform = false — buildings then never move.
        StripBackgroundFromMixedGroupParents();
        StripBackgroundFromSkyBackdrops();
        StripBackgroundFromNestedBuildingPieces();

        Transform[] transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform t = transforms[i];
            if (t == null || t.gameObject.scene != scene)
                continue;

            if (IsSkyBackdropName(t.name))
                continue;

            if (IsMixedGroupParentName(t.name))
                continue;

            // Nested grey sprites inside a light-grey-blue (or grey) root must not get their own layer.
            if (IsUnderBuildingPropRoot(t))
                continue;

            if (!TryResolveBuildingLayer(t, out int layer))
                continue;

            Background background = t.GetComponent<Background>();
            if (background == null)
                background = t.gameObject.AddComponent<Background>();

            background.ApplyParallaxLayer(
                layer,
                useSortingOrder: false,
                followHorizontal: true,
                followVertical: false);

            ApplyBuildingDrawOrder(t, layer);
        }

        // Nested strips + adds are done — force every remaining layer to re-check drive state.
        RefreshAllDriveStates();

        if (CameraFollow.Instance != null)
            CameraFollow.Instance.RefreshPlayableBounds();
    }

    private static void RefreshAllDriveStates()
    {
        Background[] all = Object.FindObjectsByType<Background>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null)
                all[i].RefreshDriveState();
        }
    }

    /// <summary>
    /// Forces consistent Order in Layer so:
    /// sky/far art (0) &lt; clouds/grey (1) &lt; regular parts (2) &lt; rocket (3) &lt; player (4).
    /// </summary>
    private static void ApplyBuildingDrawOrder(Transform root, int parallaxLayer)
    {
        if (root == null)
            return;

        int sortOrder = SortRegularParts;
        if (parallaxLayer == LayerClouds)
            sortOrder = SortClouds;
        else if (parallaxLayer == LayerLightBlueFar)
            sortOrder = SortLightBlueFar;
        else if (parallaxLayer == LayerLightBlueNear)
            sortOrder = SortLightBlueNear;
        else if (parallaxLayer == LayerGrey)
            sortOrder = SortGrey;
        else if (parallaxLayer == LayerRegularParts)
            sortOrder = SortRegularParts;

        SpriteRenderer[] renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].sortingOrder = sortOrder;
        }
    }

    private static bool IsCityScene(string sceneName)
    {
        return !string.IsNullOrEmpty(sceneName) &&
               sceneName.IndexOf("City", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void StripBackgroundFromMixedGroupParents()
    {
        DestroyBackgroundsMatching(b => IsMixedGroupParentName(b.gameObject.name));
    }

    private static void StripBackgroundFromSkyBackdrops()
    {
        DestroyBackgroundsMatching(b => IsSkyBackdropName(b.gameObject.name));
    }

    /// <summary>
    /// Removes Background from nested grey/parts pieces that sit under another building prop root
    /// (e.g. grey prefabs inside "light grey blue").
    /// </summary>
    private static void StripBackgroundFromNestedBuildingPieces()
    {
        DestroyBackgroundsMatching(b => IsUnderBuildingPropRoot(b.transform));
    }

    private static void DestroyBackgroundsMatching(System.Func<Background, bool> match)
    {
        Background[] all = Object.FindObjectsByType<Background>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            Background background = all[i];
            if (background == null || !match(background))
                continue;

            Object.DestroyImmediate(background);
        }
    }

    private static bool IsMixedGroupParentName(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
            return false;

        if (string.Equals(objectName, "City Backgrounds", System.StringComparison.OrdinalIgnoreCase))
            return true;
        if (objectName.StartsWith("City Backgrounds (", System.StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static bool IsSkyBackdropName(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
            return false;

        return objectName.IndexOf("City Sky", System.StringComparison.OrdinalIgnoreCase) >= 0
               || string.Equals(objectName, "Sky Background", System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnderBuildingPropRoot(Transform t)
    {
        if (t == null)
            return false;

        Transform p = t.parent;
        while (p != null)
        {
            if (IsBuildingPropRootName(p.name))
                return true;
            p = p.parent;
        }

        return false;
    }

    private static bool IsBuildingPropRootName(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
            return false;

        if (objectName.IndexOf("Cloud", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;
        if (IsLightGreyBlueName(objectName))
            return true;
        if (IsMidGreyName(objectName))
            return true;
        if (objectName.IndexOf("Background parts", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        return false;
    }

    private static bool IsLightGreyBlueName(string objectName)
    {
        return objectName.IndexOf("light grey blue", System.StringComparison.OrdinalIgnoreCase) >= 0
               || objectName.IndexOf("light greyish blue", System.StringComparison.OrdinalIgnoreCase) >= 0
               || objectName.IndexOf("light gray blue", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsMidGreyName(string objectName)
    {
        if (IsLightGreyBlueName(objectName))
            return false;

        return objectName.IndexOf("Backgrounds grey", System.StringComparison.OrdinalIgnoreCase) >= 0
               || objectName.IndexOf("Background grey", System.StringComparison.OrdinalIgnoreCase) >= 0
               || (objectName.IndexOf("grey", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                   objectName.IndexOf("Building", System.StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool TryResolveBuildingLayer(Transform t, out int layer)
    {
        layer = 0;
        if (t == null || string.IsNullOrEmpty(t.name))
            return false;

        string n = t.name;

        if (n.IndexOf("Cloud", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            layer = LayerClouds;
            return true;
        }

        if (IsLightGreyBlueName(n))
        {
            // Two far tiers of the same light-grey color:
            // under a City Backgrounds group → nearer far tier; otherwise → farthest buildings.
            layer = IsUnderCityBackgroundsGroup(t) ? LayerLightBlueNear : LayerLightBlueFar;
            return true;
        }

        if (IsMidGreyName(n))
        {
            layer = LayerGrey;
            return true;
        }

        if (n.IndexOf("Background parts", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            layer = LayerRegularParts;
            return true;
        }

        return false;
    }

    private static bool IsUnderCityBackgroundsGroup(Transform t)
    {
        Transform p = t.parent;
        while (p != null)
        {
            if (IsMixedGroupParentName(p.name))
                return true;
            p = p.parent;
        }

        return false;
    }
}
