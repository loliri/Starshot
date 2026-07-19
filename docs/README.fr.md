<div align="center">

<img src="../src/logo.png" width="120" alt="Logo Starshot">

# Starshot

**Outil de capture d'écran HDR natif Windows de nouvelle génération**

Capture full-pipeline 16 bits · Capture de région · Encodage AVIF / JPEG XL · Gestion des couleurs

[![Release](https://img.shields.io/github/v/release/loliri/Starshot?style=flat-square)](../../releases)
[![License](https://img.shields.io/badge/license-MIT-blue?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6?style=flat-square&logo=windows)](../../releases)

[Télécharger](../../releases) · [Démarrage rapide](#démarrage-rapide) · [Fonctionnalités](#fonctionnalités) · [Compiler depuis les sources](#compiler-depuis-les-sources)

**[简体中文](../README.md)** | **[English](README.en.md)** | **[繁體中文](README.zh-TW.md)** | **[日本語](README.ja.md)** | **Français** | **[Русский](README.ru.md)** | **[Español](README.es.md)**

</div>

---

## Pourquoi Starshot

Les outils de capture intégrés de Windows (Outil Capture d'écran, Win+Shift+S) ne peuvent capturer que des images SDR 8 bits même sur les écrans HDR — le compositeur système compresse le framebuffer HDR 16 bits, écrêtant les hautes lumières et réduisant la gamme de couleurs. Les outils tiers courants (ShareX, etc.) sont également limités par le pipeline de capture GDI/BitBlt traditionnel et ne peuvent pas accéder aux données HDR.

Starshot acquiert directement le framebuffer scRGB brut `R16G16B16A16Float` de la sortie d'affichage depuis la couche DXGI, préservant intégralement les informations de luminance HDR (jusqu'à des milliers de nits), et encode en AVIF ou JPEG XL HDR 16 bits avec les métadonnées BT.2020 + fonction de transfert PQ. Il offre également toutes les fonctionnalités attendues d'un outil de capture d'écran polyvalent : dégradation automatique pour écran SDR, capture de région, conversion par lots multi-format, et plus encore.

**Points forts**

- 🎯 **Pipeline HDR sans perte** — 16 bits tout au long de la capture, l'encodage et la gestion des couleurs ; pas de tone mapping avec perte
- 🧠 **Détection HDR/SDR intelligente** — L'histogramme maxCLL distingue le vrai contenu HDR du contenu SDR enveloppé dans un format HDR
- ✂️ **Capture de région** — Superposition multi-écran avec gel d'image, détection de fenêtre + loupe pour une sélection précise
- 📋 **Presse-papiers natif** — L'API native Win32 écrit directement dans le presse-papiers, évitant les échecs de collage liés au rendu différé WinRT
- 🗂️ **Support multi-format** — AVIF / JPEG XL / UHDR JPEG / PNG, avec outil de conversion par lots
- 📦 **Portable** — Extraire et exécuter, aucun privilège administrateur requis

<div align="center">
<table>
<tr>
<td align="center" width="50%">

**Autres outils**

<img src="https://r2.cialo.site/endfield/3840x2160.dlaa.broken.jpg" width="100%" alt="Capture SDR montrant des hautes lumières écrêtées et des couleurs délavées">
</td>
<td align="center" width="50%">

**Starshot (Ultra HDR JPEG)**

<img src="https://r2.cialo.site/endfield/3840x2160.dlaa.uhdr.jpg" width="100%" alt="Starshot Ultra HDR JPEG préservant tous les détails des hautes lumières via la gain map">
</td>
</tr>
</table>
<sub>Images tirées de *Arknights: Endfield*</sub>
</div>
</br>

> [!NOTE]
> L'Ultra HDR JPEG est affiché car GitHub ne prend pas en charge le rendu AVIF. L'AVIF original peut être consulté [ici](https://r2.cialo.site/endfield/3840x2160.dlaa.avif).

Sur les écrans SDR, Starshot utilise automatiquement le chemin de capture SDR standard comme outil de capture polyvalent ; sur les écrans HDR, c'est l'une des rares solutions de capture de bureau qui préserve intégralement les données HDR.

## Configuration requise

- Windows 11 recommandé pour la meilleure expérience
- Architecture x64
- **La capture HDR nécessite un écran HDR** (bascule automatiquement en mode SDR sur les écrans SDR)

## Téléchargement

Téléchargez l'archive depuis [Releases](../../releases), extrayez-la et exécutez `Starshot.exe` dans le répertoire racine (le lanceur démarre automatiquement le programme principal dans `app/`). Aucune installation requise — il suffit d'extraire et d'exécuter.

## Démarrage rapide

| Action                                                                              | Raccourci par défaut |
| ----------------------------------------------------------------------------------- | -------------------- |
| Capture plein écran                                                                 | Alt+W                |
| Capture de région (enregistrer le fichier + copier dans le presse-papiers)          | Alt+Q                |
| Copie de région uniquement (copier dans le presse-papiers, sans enregistrer)        | Alt+A                |

Tous les raccourcis sont personnalisables dans les paramètres.

## Fonctionnalités

### Pipeline de capture HDR

La plupart des outils de capture ne peuvent capturer que du SDR 8 bits même sur les écrans HDR — le framebuffer scRGB 16 bits en virgule flottante du compositeur système est écrasé en SDR, écrêtant les hautes lumières et réduisant la gamme de couleurs. Starshot capture le **framebuffer HDR brut** :

1. **Capture HDR** : Lorsque l'écran signale le HDR, demande le format de pixel `R16G16B16A16Float` pour obtenir les données scRGB complètes en virgule flottante (luminance jusqu'à des milliers de nits)
2. **Enregistrement HDR** : AVIF / JPEG XL 16 bits avec gamme BT.2020 + fonction de transfert PQ. Pas d'écrêtage des hautes lumières, pas de réduction de gamme
3. **Calcul maxCLL** : L'effet d'histogramme Win2D calcule le niveau de luminance maximal du contenu pour distinguer le vrai contenu HDR du contenu SDR au format HDR
4. **Gestion des couleurs** : Lit le profil ICC de l'écran pour analyser les primaires réelles de la gamme et intègre les chunks cICP/ICC dans les fichiers de sortie. HDR forcé en BT.2020 ; SDR commutable (activé = lire la gamme réelle ICC, désactivé = BT.709)

#### Traitement du contenu SDR

Sur les écrans HDR, le bureau et les applications SDR sont également capturés au format HDR (R16G16B16A16Float), mais la luminance réelle du contenu est au niveau SDR. Starshot gère cela comme suit :

- **Par défaut** : Toujours enregistré au format HDR (16 bits), **sans tone mapping 8 bits**, évitant la dégradation et les dérives de couleur
- **Option Supprimer HDR pour contenu SDR** (optionnelle) : Lorsqu'activée, détecte le seuil maxCLL — si le contenu est en dessous, le convertit automatiquement en SDR (en utilisant le format d'enregistrement SDR configuré par l'utilisateur) et supprime le fichier HDR, économisant de l'espace

#### Solution de repli UHDR JPEG

Les captures HDR peuvent également produire un Ultra HDR JPEG (image de base SDR + gain map HDR), qui s'affiche correctement même dans les logiciels sans support HDR. Encodé via le `UhdrEncoder` de `Starward.Codec`.

#### Compromis HDR de la capture de région

La superposition de capture de région tone-mappe **intentionnellement** les images HDR en SDR pour l'affichage — car le `CanvasControl` de WinUI utilise une chaîne d'échange SDR, et la sortie directe en virgule flottante scRGB apparaîtrait décolorée ou noire. **Les fichiers enregistrés sont en HDR complet**, intacts ; l'écrêtage des hautes lumières pendant la sélection n'affecte que l'aperçu, pas le résultat.

### Trois modes de capture

| Mode                      | Cible                                             | Format du presse-papiers   | Fichier enregistré |
| ------------------------- | ------------------------------------------------- | -------------------------- | ------------------ |
| Plein écran               | Écran entier (fenêtre active / écran du curseur, commutable) | CF_HDROP (fichier)         | Oui                |
| Région                    | Sélection par glissement / clic sur une fenêtre   | CF_DIB (bitmap BGRA)       | Oui                |
| Copie de région uniquement| Sélection par glissement / clic sur une fenêtre   | CF_DIB (bitmap BGRA)       | Non                |

Les trois modes partagent la détection HDR, la gestion des couleurs, les modèles de nom de fichier, le pipeline d'enregistrement et le toast d'information.

### Superposition de capture de région

- **Gel d'image** : Capture d'abord tous les écrans en un seul bitmap ; la superposition affiche une image gelée — l'écran ne bouge pas pendant la sélection, et la superposition elle-même n'est pas dans la capture
- **Multi-écran** : Couvre tout l'écran virtuel ; la loupe et la boîte de coordonnées sont limitées à l'écran du curseur (pas de débordement inter-écran)
- **Détection de fenêtre** : EnumWindows + filtrage DWM cloaked/toolwindow + limites de cadre étendu DWM (suppression des ombres) + double candidat de zone cliente + sélection par ordre Z ; cliquez sur une fenêtre pour la capturer directement (QuickCrop)
- **Loupe** : Alignement entier NearestNeighbor + grille de pixels (15×15 pixels, 10px chacun), pixels clairement distincts
- **Fourmis animées + Coordonnées en direct** : X/Y/L/H de la sélection + coordonnées physiques du curseur
- **Précision au pixel** : Sélection par glissement +1px, rectangle de fenêtre +0
- Échap / Clic droit pour annuler, Entrée pour confirmer le survol de fenêtre

### Presse-papiers

L'appel WinRT `Clipboard.SetContent` des applications WinUI non empaquetées n'est pas fiable (rendu différé + problèmes de flush, le contenu n'arrive souvent pas dans les autres applications). Starshot utilise directement les API natives Win32 (`OpenClipboard` / `SetClipboardData`) :

- **Plein écran** : CF_HDROP (format glisser-déposer de fichier) — collez dans l'Explorateur ou une application de messagerie et obtenez directement un fichier
- **Région** : CF_DIB (bitmap BGRA) — le bitmap SDR découpé de la superposition va directement dans le presse-papiers, sans lecture de fichier, sans ré-encodage, sans second tone mapping
- Appelable depuis n'importe quel thread, 10×20ms de nouvelles tentatives lorsque le presse-papiers est verrouillé

### Enregistrement

- **Structure plate** (pas de sous-dossiers), par défaut `Images\Starshot`, personnalisable
- **Format SDR** (PNG / AVIF / JPEG XL, par défaut PNG) et **Format HDR** (AVIF / JPEG XL, par défaut AVIF) définis indépendamment
- Qualité : Moyenne / Haute / Sans perte
- Métadonnées XMP (CreatorTool = Starshot)
- Encodage sérialisé (SemaphoreSlim) pour éviter les conflits d'encodage simultané
- **Statistiques de stockage** : La page des paramètres affiche l'espace disque utilisé par les captures / le cache de miniatures / les fonds d'écran / les journaux, avec actualisation et nettoyage du cache en un clic (nettoie également les fichiers de fond d'écran orphelins)

#### Formats supportés

| Format    | Profondeur de bits        | Support HDR                        | Cas d'usage                         |
| --------- | ------------------------- | ---------------------------------- | ----------------------------------- |
| PNG       | 8 bits / 16 bits          | Peut enregistrer HDR mais compatibilité faible | SDR par défaut, sans perte          |
| AVIF      | 8 bits / 10 bits / 12 bits| HDR complet                       | HDR par défaut, compression élevée  |
| JPEG XL   | 8 bits / 16 bits          | HDR complet                       | Alternative HDR, compression réversible |
| UHDR JPEG | 8 bits + gain map         | Solution de repli HDR compatible SDR | Sortie HDR supplémentaire           |

### Modèles de nom de fichier

Les captures plein écran et de région utilisent des **modèles indépendants**.

| Paramètre substituable                                     | Signification                                | Exemple             |
| ---------------------------------------------------------- | -------------------------------------------- | ------------------- |
| `{process}`                                                | Nom du processus (sans extension)            | `explorer`          |
| `{processPath}`                                            | Nom du fichier exe (avec extension)          | `explorer.exe`      |
| `{title}`                                                  | Titre de la fenêtre (tronqué, longueur réglable) | `Genshin Impact` |
| `{timestamp}`                                              | Horodatage Unix                              | `1721234567`        |
| `{time}`                                                   | yyyyMMdd_HHmmssff                            | `20260718_14302512` |
| `{date}`                                                   | yyyyMMdd                                     | `20260718`          |
| `{width}` `{height}`                                       | Dimensions de l'image (px)                   | `1920` `1080`       |
| `{year}` `{month}` `{day}` `{hour}` `{minute}` `{second}`  | Composants individuels de l'heure            |                     |

Les caractères illégaux dans les noms de fichiers sont uniformément remplacés par `_`.

### Toast d'information

Après une capture, une miniature + un toast d'état apparaît (n'affecte pas les captures — défini avec `WDA_EXCLUDEFROMCAPTURE`, les autres outils de capture ne peuvent pas voir cette fenêtre) :

- **En cours** (animation de rotation) / **Enregistré** (avec bouton ouvrir) / **Copié** (coche verte) / **Échec**
- Compteur de captures multiples (ex. 2/3)
- Animation Composition de glissement entrant/sortant

### Bibliothèque de captures

- Navigation multi-dossiers (répertoire de captures par défaut + dossiers ajoutés par l'utilisateur)
- `FileSystemWatcher` détecte les ajouts/suppressions en temps réel
- Groupé par date, miniatures chargées à la demande
- Menu contextuel : Ouvrir / Copier le fichier / Copier en JPG / Ouvrir dans l'Explorateur / Ouvrir avec / Supprimer
- Sélection multiple + glisser-déposer + entrée de conversion par lots

### Visionneuse d'images

- Zoom (curseur / boutons / molette de souris / double-clic pour adapter), mode plein écran (F11)
- Précédent / Suivant (touches fléchées, molette de souris, bande de miniatures en bas)
- Glisser-déposer des fichiers pour ouvrir
- Menu contextuel : Copier le fichier / le chemin / l'image, Supprimer, Ouvrir dans l'Explorateur, Ouvrir avec
- **Panneau d'édition** : Bascule du mode d'affichage HDR / SDR / Auto, curseur de luminosité SDR (100–500 nits), informations sur l'image et l'écran
- **Conversion de format** : Exporter vers PNG / AVIF / JPEG XL (écran SDR) ou UHDR JPEG / AVIF / JPEG XL (écran HDR)
- **Gestion des couleurs** : Lit le profil ICC de l'écran et AdvancedColorInfo

### Conversion par lots de formats

| Direction de conversion              | Moteur                                  |
| ------------------------------------ | --------------------------------------- |
| JPG / PNG → AVIF / JXL               | avifenc.exe / cjxl.exe (CLI)            |
| AVIF / JXL → JPG / PNG               | avifdec.exe / djxl.exe (CLI)            |
| JXR / WEBP / HEIC etc. → AVIF / JXL  | ImageSaver en processus (avifEncoderLite)|


### Personnalisation

- **Fond d'écran personnalisé** : Trois modes
  - **Image fixe** : Choisissez une image, affichée en permanence
  - **Vidéo fixe** : Lecture en boucle muette, mise en pause automatique lorsque la fenêtre principale est masquée
  - **Dossier aléatoire** : Choisit aléatoirement une image ou une vidéo dans un dossier à chaque lancement
  - Détection automatique des sources de fond d'écran manquantes, nettoyage de la configuration et retour à l'absence de fond d'écran + notification toast
- **Couleur d'accentuation** :
  - **Extraction automatique depuis le fond d'écran** (activée par défaut) : Échantillonne la couleur dominante du fond d'écran comme couleur d'accentuation de l'application (boost de saturation HSV) ; les vidéos n'échantillonnent que la première image pour éviter le scintillement des couleurs
  - **Couleur personnalisée** : Le sélecteur de couleur manuel remplace l'extraction automatique
- **Thème** : Suivre le système / Clair / Sombre
- **Effet acrylique** : En mode fond d'écran, choisissez entre une couche de verre dépoli ou la transparence directe du fond d'écran

### Écran de démarrage

Affiche le logo + le slogan au démarrage, avec un délai de 700 ms suivi d'un fondu de 400 ms. Se déclenche uniquement à la première ouverture de fenêtre ; ne se rejoue pas lors de la restauration depuis la barre d'état système.

### Barre d'état système

- Clic gauche pour afficher la fenêtre principale, clic droit pour le menu contextuel (Afficher / Quitter)
- La fermeture de la fenêtre principale minimise dans la barre d'état système (commutable)
- Le mécanisme `ForceExit` garantit que « Quitter » depuis la barre d'état système quitte réellement

### Démarrage automatique

- Registre `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, pointant vers le lanceur (racine `Starshot.exe`)
- Option `--hide` pour démarrer minimisé dans la barre d'état système (nécessite que la barre d'état système soit activée)

## Architecture

### Structure des répertoires

```
Racine/
  Starshot.exe            ← Lanceur C++ (~400 Ko, lance le programme principal app/)
  StarshotDatabase.db     ← Base de données SQLite des paramètres
  app/
    Starshot.exe          ← Programme principal (WinUI 3 / .NET 10)
    *.dll                 ← Dépendances
    avifenc.exe etc.      ← Outils de codec (depuis Starward.Codec NuGet)
%LOCALAPPDATA%/Starshot/  (par défaut, configurable)
  log/                    ← Journaux
  cache/                  ← Cache de miniatures
```

### Lanceur

Programme natif C++ (~400 Ko), actuellement codé en dur vers `app/Starshot.exe`. Prévu pour le futur : prise en charge de `version.ini` pour des répertoires versionnés + nettoyage automatique des anciennes versions.

### Stack technique

| Couche                 | Technologie                                                              |
| ---------------------- | ------------------------------------------------------------------------ |
| Framework UI           | WinUI 3 (Windows App SDK 1.8)                                            |
| Runtime                | .NET 10                                                                  |
| Graphisme              | Win2D 1.3 (interopérabilité D3D11, tone mapping HDR, effet d'histogramme)|
| Codec                  | Starward.Codec NuGet (wrapper P/Invoke libavif / libjxl / UltraHDR)      |
| Stockage de données    | SQLite + Dapper                                                          |
| Journalisation         | Serilog                                                                  |
| Barre d'état système   | H.NotifyIcon.WinUI                                                       |
| Miniatures             | Scighost.WinUI ImageEx + CachedImage personnalisé                        |
| Superposition de région| Win2D CanvasControl (rendu d'image gelée + dessin de sélection)          |
| Presse-papiers         | API native Win32 (OpenClipboard / SetClipboardData)                      |
| Lanceur                | C++ natif (toolset v145, CRT statique)                                   |

### Protection contre la réentrance

Garde globale `Interlocked.CompareExchange` — les modes plein écran, région et copie uniquement partagent un seul drapeau `_isCapturing`, empêchant les captures multiples dues à des frappes rapides ou des pressions consécutives de raccourcis.

### Configuration de build

|                       | Debug                                       | Release                                                                                                       |
| --------------------- | ------------------------------------------- | ------------------------------------------------------------------------------------------------------------- |
| Runtime .NET          | Framework-dependent (non empaqueté)         | Autonome                                                                                                      |
| Bibliothèques natives | win-x64 uniquement (RuntimeIdentifier, à plat dans la racine de sortie) | Identique à Debug                                                                              |
| Trim                  | Non                                         | Partiel                                                                                                       |
| ReadyToRun            | Non                                         | Oui                                                                                                           |
| Nettoyage supplémentaire | —                                        | Supprime DirectML.dll / onnxruntime.dll / NpuDetect (composants WinML/AI du Windows App SDK, non utilisés)    |
| Chemin de sortie      | `build/app/`                                | `build/release/app/` + lanceur copié dans `build/release/`                                                    |
| Taille                | ~80 Mo                                      | Plus petit (Trim + suppression des bibliothèques AI)                                                          |

## Compiler depuis les sources

### Prérequis

- Visual Studio 2022 / 2026 (avec développement bureau C++ et développement bureau .NET)
- SDK .NET 10
- Windows SDK 10.0.26100

### Étapes

```bash
git clone https://github.com/loliri/Starshot
cd Starshot

# === Debug ===
# Compiler le programme principal (sortie vers build/app/)
dotnet build src/Starshot/Starshot.csproj -c Debug -p:Platform=x64

# Compiler le lanceur (sortie vers build/Starshot.exe, nécessite MSBuild de VS)
"C:\Program Files\Microsoft Visual Studio\<version>\Community\MSBuild\Current\Bin\MSBuild.exe" src/Starshot.Launcher/Starshot.Launcher.vcxproj -p:Configuration=Release -p:Platform=x64

# Exécuter : build/Starshot.exe (lanceur) ou build/app/Starshot.exe (programme principal)

# === Publication Release ===
# 1. Compiler d'abord le lanceur (sortie vers build/Starshot.exe)
"C:\Program Files\Microsoft Visual Studio\<version>\Community\MSBuild\Current\Bin\MSBuild.exe" src/Starshot.Launcher/Starshot.Launcher.vcxproj -p:Configuration=Release -p:Platform=x64

# 2. Publier le programme principal (sortie vers build/release/app/, copie automatique du lanceur vers build/release/Starshot.exe + suppression des bibliothèques AI)
dotnet publish src/Starshot/Starshot.csproj -c Release -p:Platform=x64

# Structure de répertoires résultante :
# build/release/
#   Starshot.exe        ← Lanceur (copié automatiquement)
#   app/
#     Starshot.exe      ← Programme principal (autonome + trim + R2R)
#     *.dll / avifenc.exe etc.
```

## Limitations connues

- La superposition de capture de région affiche les images HDR en SDR (WinUI CanvasControl utilise une chaîne d'échange SDR) ; les fichiers enregistrés ne sont pas affectés
- Le fond d'écran personnalisé utilise le remplissage `UniformToFill`, mais le recadrage de WinUI n'est pas centré — actuellement aligné **en haut à gauche**, par exemple un fond d'écran étroit (portrait) sur une fenêtre large n'affichera que la partie supérieure (recadrée depuis le haut, pas le centre)
- Lorsque la superposition de capture de région s'ouvre, le curseur reste en forme par défaut du système ; **déplacez la souris une fois** pour que le curseur en croix apparaisse (le `ProtectedCursor` de WinUI ne s'applique pas immédiatement à un pointeur stationnaire déjà sur l'élément — le déplacer une fois déclenche un événement de pointeur, après quoi il fonctionne normalement)
- Pas encore de gestion de version / mise à jour automatique

## Remerciements

- [Starward](https://github.com/Scighost/Starward) — Le cœur de capture, le moteur de codec et le framework de fenêtre sont tous dérivés de Starward, développé par [@Scighost](https://github.com/Scighost)
- [ShareX](https://github.com/ShareX/ShareX) — Référence pour la détection de fenêtre et la conception d'interaction de la superposition de capture de région

## Licence

MIT
