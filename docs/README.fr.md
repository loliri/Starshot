<div align="center">

<img src="../src/logo.png" width="120" alt="Logo Starshot">

# Starshot

**Outil de capture d'écran HDR natif nouvelle génération pour Windows**

**Next-generation Windows-native HDR Screenshot Tool**

Capture 16bit full pipeline · Capture régionale · Encodage AVIF / JPEG XL · Gestion des couleurs

[![Release](https://img.shields.io/github/v/release/loliri/Starshot?style=flat-square)](../../../releases)
[![License](https://img.shields.io/badge/license-MIT-blue?style=flat-square)](https://github.com/loliri/Starshot?tab=MIT-1-ov-file)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6?style=flat-square&logo=windows)](../../../releases)

[Télécharger](../../../releases) · [Démarrage rapide](#démarrage-rapide) · [Fonctionnalités](#fonctionnalités) · [Compiler depuis les sources](#compiler-depuis-les-sources)

**[English](../README.md)** | **[简体中文](README.zh-CN.md)** | **[繁體中文](README.zh-TW.md)** | **[日本語](README.ja.md)** | **Français** | **[Русский](README.ru.md)** | **[Español](README.es.md)**

</div>

---

## Pourquoi Starshot

Les outils de capture intégrés de Windows (Snipping Tool, Win+Shift+S) ne produisent que des images SDR 8bit même sur les écrans HDR — le compositeur système écrase les trames HDR 16bit en sortie, les hautes lumières sont écrêtées et la gamme de couleurs est réduite. Les outils de capture courants (ShareX, etc.) sont également limités par le pipeline traditionnel GDI/BitBlt et ne peuvent pas accéder aux données HDR.

Starshot capture directement le framebuffer brut `R16G16B16A16Float` scRGB depuis la couche DXGI, préservant intégralement les informations de luminance HDR (jusqu'à plusieurs milliers de nits). Les captures sont encodées en AVIF ou JPEG XL HDR 16bit, avec les métadonnées d'espace colorimétrique BT.2020 et de fonction de transfert PQ. L'outil offre également une dégradation automatique pour écrans SDR, une capture régionale, une conversion par lots multi-format et toutes les fonctionnalités attendues d'un outil de capture généraliste.

**Caractéristiques principales**

- 🎯 **Pipeline HDR sans perte** — Capture, encodage et gestion des couleurs en 16bit de bout en bout. Aucun tone mapping avec perte.
- 🧠 **Détection intelligente HDR/SDR** — Distingue automatiquement le contenu réellement HDR du contenu SDR encapsulé dans un format HDR, sans gaspiller d'espace.
- ✂️ **Capture régionale** — Overlay multi-écran avec gel d'image, détection de fenêtres et loupe pour une sélection précise au pixel près.
- 📋 **Presse-papiers natif** — API native Win32 pour une écriture directe, collage fiable sans perte de contenu.
- 🗂️ **Support multi-format** — AVIF / JPEG XL / UHDR JPEG / PNG, avec outil de conversion par lots.
- 🖥️ **Multi-écran** — La capture régionale peut sélectionner à travers plusieurs écrans, composant directement des images traversant les frontières d'écrans.
- 🔄 **Vérification automatique de mises à jour** — Vérification intégrée ; nouvelle version détectée → téléchargement en flux, extraction et remplacement.

<div align="center">
<table>
<tr>
<td align="center" width="50%">

**Autres outils**

<img src="https://r2.cialo.site/endfield/3840x2160.dlaa.broken.jpg" width="100%" alt="Capture SDR montrant des hautes lumières écrêtées et des couleurs délavées">
</td>
<td align="center" width="50%">

**Starshot (Ultra HDR JPEG)**

<img src="https://r2.cialo.site/endfield/3840x2160.dlaa.uhdr.jpg" width="100%" alt="Capture Starshot Ultra HDR JPEG préservant tous les détails des hautes lumières via la gain map">
</td>
</tr>
</table>
<sub>Images tirées de *Arknights: Endfield*</sub>
</div>
</br>

> [!NOTE]
> GitHub ne prenant pas en charge le rendu AVIF, la comparaison ci-dessus utilise Ultra HDR JPEG. L'image AVIF originale est consultable [ici](https://r2.cialo.site/endfield/3840x2160.dlaa.avif).

Sur un écran SDR, Starshot emprunte automatiquement le chemin de capture SDR standard et fonctionne comme un outil de capture généraliste. Sur un écran HDR, c'est l'une des rares solutions de capture de bureau capables de préserver intégralement les données HDR.

## Configuration requise

- Windows 10 / 11, Windows 11 recommandé pour une expérience optimale
- Architecture x64
- **Un écran HDR est requis pour la capture HDR** (bascule automatiquement en mode SDR sur les écrans SDR)

## Téléchargement

Téléchargez l'archive depuis les [Releases](../../../releases), extrayez-la et lancez `Starshot.exe` depuis le répertoire racine. Aucune installation nécessaire — il suffit d'extraire et de lancer.

## Captures d'écran

![Screenshot](Screenshot.jpg)

## Démarrage rapide

| Action                                                                           | Raccourci par défaut |
| -------------------------------------------------------------------------------- | -------------------- |
| Capture plein écran                                                              | Alt+W                |
| Capture régionale (sauvegarde + copie dans le presse-papiers après sélection)    | Alt+Q                |
| Copie régionale seule (copie dans le presse-papiers uniquement, sans sauvegarde) | Alt+A                |

Tous les raccourcis sont personnalisables dans les paramètres.

## Fonctionnalités

### Pipeline de capture HDR

La plupart des outils de capture ne produisent que du SDR 8bit même sur les écrans HDR — la trame scRGB flottante 16bit du compositeur système est écrasée en SDR avec des hautes lumières écrêtées et une gamme réduite. Starshot capture le **framebuffer HDR brut** :

1. **Capture HDR** : Lorsque l'écran signale le mode HDR, demande le format de pixel `R16G16B16A16Float` pour obtenir les données scRGB flottantes complètes (luminance jusqu'à plusieurs milliers de nits)
2. **Sauvegarde HDR** : AVIF / JPEG XL 16bit, espace BT.2020 + fonction de transfert PQ. Les hautes lumières ne sont pas écrêtées, la gamme n'est pas réduite
3. **Calcul maxCLL** : L'effet d'histogramme Win2D calcule la luminance maximale du contenu, utilisée pour distinguer le contenu réellement HDR du contenu SDR en conteneur HDR
4. **Gestion des couleurs** : Lecture du profil ICC de l'écran pour extraire les primaires de gamme réelles, écriture des chunks cICP/ICC dans le fichier. HDR force BT.2020 ; la gestion des couleurs SDR est activable (activé = lecture ICC gamme réelle, désactivé = BT.709)

#### Traitement du contenu SDR

Sur un écran HDR, le bureau et les applications SDR sont également capturés au format HDR (R16G16B16A16Float), mais la luminance réelle du contenu est au niveau SDR. Starshot gère cela comme suit :

- **Par défaut** : Toujours sauvegardé au format HDR (16bit), **sans tone mapping 8bit**, évitant la dégradation et les dérives de couleur
- **Option Supprimer le HDR pour le contenu SDR** (optionnel) : Activée, détecte le seuil maxCLL et convertit automatiquement en SDR (selon le format SDR configuré par l'utilisateur) puis supprime le fichier HDR pour économiser de l'espace

#### Solution de repli UHDR JPEG

Les captures HDR peuvent également produire un Ultra HDR JPEG (image de base SDR + gain map HDR), qui s'affiche correctement même dans les logiciels ne prenant pas en charge le HDR. Encodé via `UhdrEncoder` de `Starward.Codec`.

#### Compromis HDR de la capture régionale

L'overlay de capture régionale applique **intentionnellement** un tone mapping HDR vers SDR pour l'affichage — car `CanvasControl` de WinUI utilise une chaîne d'échange SDR, et la sortie directe en virgule flottante scRGB produirait des dérives de couleur ou un noircissement. **Le fichier sauvegardé est en HDR intégral**, intact ; la compression des hautes lumières durant la sélection n'affecte que l'aperçu, jamais la sortie.

### Trois modes de capture

| Mode                 | Cible                                                     | Format presse-papiers     | Fichier    |
| -------------------- | --------------------------------------------------------- | ------------------------- | ---------- |
| Plein écran          | Moniteur entier (fenêtre au premier plan / écran du curseur, commutable) | CF_HDROP (fichier)        | Sauvegardé |
| Région               | Sélection rectangulaire / clic sur une fenêtre            | CF_DIB (bitmap BGRA)      | Sauvegardé |
| Copie région seule   | Sélection rectangulaire / clic sur une fenêtre            | CF_DIB (bitmap BGRA)      | Non        |

Les trois modes partagent la même détection HDR, la gestion des couleurs, les modèles de nom de fichier, le pipeline de sauvegarde et le toast d'information.

### Overlay de capture régionale

- **Gel d'image** : Capture d'abord tous les moniteurs en une seule bitmap assemblée ; l'overlay affiche cette image gelée afin que l'image reste fixe pendant la sélection. L'overlay lui-même est exclu de la capture.
- **Multi-écran** : Couvre tout l'écran virtuel. La sélection peut s'étendre sur plusieurs écrans (luminosité exacte même en configuration HDR+SDR mixte) ; la loupe et la boîte de coordonnées sont limitées au moniteur du curseur.
- **Détection de fenêtres** : EnumWindows + filtrage DWM cloaked/toolwindow + bordures étendues DWM (suppression des ombres) + double candidat zone cliente + sélection par Z-order. Cliquez sur une fenêtre pour la capturer directement (QuickCrop).
- **Loupe** : Alignement entier NearestNeighbor + grille de pixels (15×15 pixels, 10px chacun), rendant les pixels individuels clairement distinguables.
- **Ligne de sélection animée + coordonnées en temps réel** : X/Y/L/H de la sélection + coordonnées physiques du curseur.
- **Précision au pixel** : Sélection par glissement +1px ; rectangle de fenêtre +0.
- Échap / Clic droit pour annuler ; Entrée pour confirmer la fenêtre survolée.

### Presse-papiers

Le `Clipboard.SetContent` WinRT des applications WinUI non empaquetées n'est pas fiable (rendu différé + problèmes de Flush, le contenu n'arrive souvent pas dans les autres applications). Starshot utilise directement les API natives Win32 (`OpenClipboard` / `SetClipboardData`) :

- **Capture plein écran** : CF_HDROP (format de glisser-déposer de fichier) — collez dans l'Explorateur ou une application de messagerie pour obtenir directement le fichier
- **Capture régionale** : CF_DIB (bitmap BGRA) — le bitmap SDR découpé de l'overlay est placé directement dans le presse-papiers, sans lecture de fichier, sans ré-encodage, sans second tone mapping
- Appelable depuis n'importe quel thread, avec 10×20ms de nouvelles tentatives en cas de conflit d'accès au presse-papiers

### Sauvegarde

- **Structure à plat** (sans sous-dossiers). Par défaut `Images\Starshot`, personnalisable.
- **Format SDR** (PNG / AVIF / JPEG XL ; défaut PNG) et **format HDR** (AVIF / JPEG XL ; défaut AVIF) configurés séparément.
- Niveaux de qualité : Moyen / Élevé / Sans perte.
- Métadonnées XMP (CreatorTool = Starshot).
- Encodage sérialisé (SemaphoreSlim) pour éviter les conflits d'encodage simultané.
- **Statistiques de stockage** : La page des paramètres affiche l'espace disque utilisé par les captures / le cache de miniatures / les fonds d'écran / les journaux / les sauvegardes, avec actualisation et nettoyage du cache en un clic (nettoie également les fichiers de fond d'écran orphelins).

#### Formats supportés

| Format     | Profondeur de bits         | Support HDR                         | Cas d'usage                        |
| ---------- | -------------------------- | ----------------------------------- | ---------------------------------- |
| PNG        | 8bit / 16bit               | Stockable mais mauvaise compatibilité | SDR par défaut, sans perte         |
| AVIF       | 8bit / 10bit / 12bit       | HDR complet                         | HDR par défaut, haute compression  |
| JPEG XL    | 8bit / 16bit               | HDR complet                         | Alternative HDR, compression réversible |
| UHDR JPEG  | 8bit + gain map            | Solution de repli HDR compatible SDR | Sortie HDR supplémentaire          |

### Modèles de nom de fichier

Les captures plein écran et régionales utilisent des **modèles indépendants**.

| Emplacement                                                 | Signification                                  | Exemple             |
| ----------------------------------------------------------- | ---------------------------------------------- | ------------------- |
| `{process}`                                                 | Nom du processus (sans extension)              | `explorer`          |
| `{processPath}`                                             | Nom du fichier exe (avec extension)            | `explorer.exe`      |
| `{title}`                                                   | Titre de la fenêtre (trim + longueur max configurable) | `Genshin Impact`    |
| `{timestamp}`                                               | Horodatage Unix                                | `1721234567`        |
| `{time}`                                                    | yyyyMMdd_HHmmssff                              | `20260718_14302512` |
| `{date}`                                                    | yyyyMMdd                                       | `20260718`          |
| `{width}` `{height}`                                        | Dimensions de l'image (px)                     | `1920` `1080`       |
| `{year}` `{month}` `{day}` `{hour}` `{minute}` `{second}`   | Composants de la date/heure                    |                     |

Les caractères illégaux dans les noms de fichier sont uniformément remplacés par `_`.

### Toast d'information

Après une capture, un toast avec miniature + statut apparaît (n'interfère pas avec les captures — `WDA_EXCLUDEFROMCAPTURE` est défini, les autres outils de capture ne peuvent pas capturer cette fenêtre) :

- **En cours** (animation de rotation) / **Sauvegardé** (avec bouton Ouvrir) / **Copié** (coche verte) / **Échec**
- Compteur de prises en rafale (ex. 2/3)
- Animations de glissement Composition (entrée/sortie)

### Bibliothèque de captures

- Navigation multi-dossiers (répertoire de captures par défaut + dossiers ajoutés par l'utilisateur)
- `FileSystemWatcher` pour la détection en temps réel des ajouts/suppressions
- Regroupement par date, chargement différé des miniatures
- Menu contextuel : Ouvrir / Copier le fichier / Copier en JPG / Ouvrir dans l'Explorateur / Ouvrir avec / Supprimer
- Sélection multiple + glisser-déposer + point d'entrée de conversion par lots

### Visualiseur d'images

- Zoom (curseur / boutons / molette de la souris / double-clic pour ajuster), mode plein écran (F11)
- Précédent / Suivant (touches fléchées, molette de la souris, bande de miniatures en bas)
- Glisser-déposer des fichiers pour les ouvrir directement
- Menu contextuel : Copier le fichier / le chemin / l'image, Supprimer, Ouvrir dans l'Explorateur, Ouvrir avec
- **Panneau d'édition** : Bascule du mode d'affichage HDR / SDR / Auto, curseur de luminosité SDR (100–500 nits), informations sur l'image et l'écran
- **Conversion de format** : Exporter en PNG / AVIF / JPEG XL (écran SDR) ou UHDR JPEG / AVIF / JPEG XL (écran HDR)
- **Gestion des couleurs** : Lecture du profil ICC de l'écran et AdvancedColorInfo

### Conversion par lots

| Direction de conversion               | Moteur                                |
| ------------------------------------- | ------------------------------------- |
| JPG / PNG → AVIF / JXL                | avifenc.exe / cjxl.exe (CLI)          |
| AVIF / JXL → JPG / PNG                | avifdec.exe / djxl.exe (CLI)          |
| JXR / WEBP / HEIC etc. → AVIF / JXL   | ImageSaver en processus (avifEncoderLite) |

### Personnalisation

- **Fond d'écran personnalisé** : Trois modes
  - **Image spécifique** : Choisir une image, affichée en permanence
  - **Vidéo spécifique** : Lecture en boucle muette ; pause automatique quand la fenêtre principale est masquée
  - **Aléatoire depuis un dossier** : Choisit une image ou vidéo aléatoire dans un dossier à chaque lancement
  - Détection automatique de la perte de source du fond d'écran, nettoyage de la configuration et retour à l'absence de fond d'écran + notification toast
- **Couleur d'accentuation** :
  - **Extraction automatique depuis le fond d'écran** (activé par défaut) : Échantillonne la couleur dominante du fond d'écran comme couleur d'accentuation (boost de saturation HSV). Pour les vidéos, seule la première image est échantillonnée pour éviter le scintillement.
  - **Couleur personnalisée** : Le sélecteur de couleur manuel remplace l'extraction automatique
- **Thème** : Suivre le système / Clair / Sombre
- **Effet acrylique** : En mode fond d'écran, choix entre une couche de verre dépoli ou une transparence directe du fond d'écran

### Écran de démarrage

Affiche le logo + le slogan au lancement. Délai de 700ms puis fondu en 400ms. Se déclenche uniquement à la première ouverture de la fenêtre ; ne se rejoue pas lors de la restauration depuis la barre des tâches.

### Barre des tâches système

- Clic gauche pour afficher la fenêtre principale, clic droit pour le menu contextuel (Afficher / Quitter)
- La fermeture de la fenêtre principale minimise dans la barre des tâches (désactivable)
- Le mécanisme `ForceExit` garantit que « Quitter » depuis la barre des tâches ferme réellement l'application

### Démarrage automatique

- Clé de registre `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, pointant vers le lanceur (`Starshot.exe` à la racine)
- Option `--hide` pour démarrer minimisé dans la barre des tâches (nécessite que la barre des tâches soit activée)
- Le bouton lit le registre en temps réel (pas de cache en base de données) : la désactivation depuis le Gestionnaire des tâches ne modifie que StartupApproved sans supprimer l'entrée Run — le bouton reste sur Activé
- Au démarrage, vérifie si l'exe pointé par l'entrée de démarrage automatique existe ; s'il n'existe pas, supprime automatiquement l'entrée et affiche une notification toast

### Vérification des mises à jour

- Vérification limitée au démarrage (≥24h + option activée) de la dernière version sur GitHub Releases, ou vérification manuelle depuis la page À propos
- Les mises à jour utilisent la décompression en streaming véritable de SharpCompress (flux réseau directement connecté, sans sauvegarde du zip sur disque). Chaque entrée est écrite directement dans le répertoire racine. En cas d'échec, l'état précédent est restauré. En cas de succès, le lanceur redémarre avec `--clean` pour nettoyer les anciennes versions
- Vérifie uniquement les releases CI/CD (lit le numéro de version dans `version.ini`). Les builds locaux (sans `version.ini`, `AppVersion = Local`) sont traités comme 0.0.0 et peuvent se mettre à jour vers n'importe quel release CI/CD
- Convention de casse de version : le tag GitHub, le nom du zip et le répertoire `app-{version}/` sont en minuscules (ex. `0.3.1-preview`) ; `version.ini` conserve la casse d'origine (`0.3.1-Preview`, affiché dans À propos), et le lanceur le passe en minuscules pour localiser le répertoire.

## Limitations connues

- L'overlay de capture régionale affiche les trames HDR en SDR (CanvasControl de WinUI utilise une chaîne d'échange SDR) ; les fichiers sauvegardés ne sont pas affectés
- Les fonds d'écran personnalisés utilisent `UniformToFill` pour couvrir la fenêtre, mais le recadrage de WinUI n'est pas centré — il est actuellement aligné en **haut à gauche**. Par exemple, un fond d'écran étroit (portrait) dans une fenêtre large n'affichera que la partie supérieure (recadrée depuis le haut plutôt que centrée)
- Au moment où l'overlay de capture régionale s'ouvre, le curseur reste dans sa forme par défaut. **Il faut bouger la souris une fois** pour que le curseur en croix apparaisse (le `ProtectedCursor` de WinUI ne prend pas effet immédiatement sur un pointeur immobile déjà au-dessus de l'élément ; un mouvement déclenche un événement de pointeur, après quoi tout fonctionne normalement)
- Sur un double écran avec DPI différents (ex. primaire 150 %, secondaire 125 %), la **détection des fenêtres** de la capture régionale (survol pour sélectionner une fenêtre) est décalée sur l'écran secondaire ; la sélection libre par glissement et la sauvegarde ne sont pas affectées. Solution : utiliser la même échelle sur les deux écrans
- Lors de la capture régionale, le survol de certaines fenêtres peut afficher des valeurs négatives dans la boîte de coordonnées (ex. `-11,-11`). Ce sont les limites du cadre étendu de la fenêtre rapportées par Windows DWM (incluant l'ombre/bordure hors écran) ; Starshot les lit telles quelles — la partie hors écran est invisible et n'affecte pas la capture

## Architecture

### Structure des répertoires

```
Racine/
  Starshot.exe            ← Lanceur C++ (lit version.ini pour décider quel répertoire app lancer)
  StarshotDatabase.db     ← Base de données SQLite des paramètres
  version.ini             ← Numéro de version (releases CI/CD uniquement ; absent dans les builds locaux)
  app-{version}/          ← Répertoire du programme principal (versionné pour les releases CI/CD, app/ pour les builds locaux)
    Starshot.exe          ← Programme principal (WinUI 3 / .NET 10)
    *.dll                 ← Dépendances
    avifenc.exe etc.      ← Outils de codec (depuis Starward.Codec NuGet)
  backup/                 ← Sauvegardes de la base de données
%LOCALAPPDATA%/Starshot/  (par défaut, configurable)
  log/                    ← Journaux
  bg/                     ← Fonds d'écran
  thumb/                  ← Cache de miniatures
```

### Lanceur

Programme natif C++ (~400 Ko). Lit `version.ini` pour décider de lancer `app-{version}/Starshot.exe` (si absent, utilise `app/` pour les builds debug/local). Lorsqu'il est lancé avec `--clean` (ou `--clean=<pid>`), parcourt les répertoires `app-*` et supprime ceux qui ne correspondent pas à la version actuelle.

### Barre des tâches et démarrage en arrière-plan

- `--hide` : Au démarrage automatique, MainWindow n'est pas créée. Les raccourcis globaux sont enregistrés sur le hwnd de SystemTrayWindow (la fenêtre de la barre des tâches sert d'hôte persistant)
- Le TaskbarIcon de H.NotifyIcon.WinUI nécessite un Window.Show pour déclencher `Loaded` avant d'enregistrer l'icône. Lors de l'initialisation, `WS_EX_LAYERED + alpha=0` rend la fenêtre transparente pour effectuer ce Show, évitant un flash visible lors du démarrage automatique avec `--hide`
- Le lanceur C++ recombine `argv[1..]` pour transmettre les arguments de ligne de commande

### Stack technique

| Couche                    | Technologie                                                             |
| ------------------------- | ----------------------------------------------------------------------- |
| Framework UI              | WinUI 3 (Windows App SDK 1.8)                                           |
| Runtime                   | .NET 10                                                                 |
| Graphisme                 | Win2D 1.3 (interopérabilité D3D11, tone mapping HDR, effets d'histogramme) |
| Codecs                    | Starward.Codec NuGet (wrapper P/Invoke libavif / libjxl / UltraHDR)     |
| Stockage de données       | SQLite + Dapper                                                         |
| Journalisation            | Serilog                                                                 |
| Barre des tâches          | H.NotifyIcon.WinUI                                                      |
| Miniatures                | Scighost.WinUI ImageEx + CachedImage personnalisé                       |
| Overlay de région         | Win2D CanvasControl (rendu d'image gelée + dessin de sélection)         |
| Presse-papiers            | API native Win32 (OpenClipboard / SetClipboardData)                     |
| Lanceur                   | C++ natif (toolset v145, CRT statique)                                  |

### Protection contre la réentrance

Garde globale `Interlocked.CompareExchange`. Les modes plein écran, région et copie seule partagent un seul drapeau `_isCapturing` — les répétitions rapides de touches ou les pressions consécutives de raccourcis ne déclenchent pas de captures multiples.

### Configuration de build

|                    | Debug                                          | Release                                                                                                |
| ------------------ | ---------------------------------------------- | ------------------------------------------------------------------------------------------------------ |
| Runtime .NET       | Framework-dependent (non autonome)             | Autonome                                                                                               |
| Bibliothèques natives | win-x64 uniquement (RuntimeIdentifier, à plat dans la racine de sortie) | Identique à Debug                                                                                      |
| Trim               | Non                                            | Partiel                                                                                                |
| ReadyToRun         | Non                                            | Oui                                                                                                    |
| Nettoyage suppl.   | —                                              | Supprime DirectML.dll / onnxruntime.dll / NpuDetect (composants WinML/AI du Windows App SDK, non utilisés) |
| Chemin de sortie   | `build/app/`                                   | `build/release/app/` + lanceur copié dans `build/release/`                                             |
| Taille             | ~80 Mo                                         | Plus petite (Trim + suppression des bibliothèques IA)                                                  |

## Compiler depuis les sources

### Prérequis

- Visual Studio 2022 / 2026 (avec Développement Desktop C++ et Développement Desktop .NET)
- SDK .NET 10
- Windows SDK 10.0.26100

### Étapes

```bash
git clone https://github.com/loliri/Starshot
cd Starshot

# === Debug ===
# Compiler le programme principal (sortie dans build/app/)
dotnet build src/Starshot/Starshot.csproj -c Debug -p:Platform=x64

# Compiler le lanceur (sortie dans build/Starshot.exe ; nécessite MSBuild de VS)
"C:\Program Files\Microsoft Visual Studio\<version>\Community\MSBuild\Current\Bin\MSBuild.exe" src/Starshot.Launcher/Starshot.Launcher.vcxproj -p:Configuration=Release -p:Platform=x64

# Lancer : build/Starshot.exe (lanceur) ou build/app/Starshot.exe (programme principal)

# === Publication Release ===
# 1. Compiler d'abord le lanceur (sortie dans build/Starshot.exe)
"C:\Program Files\Microsoft Visual Studio\<version>\Community\MSBuild\Current\Bin\MSBuild.exe" src/Starshot.Launcher/Starshot.Launcher.vcxproj -p:Configuration=Release -p:Platform=x64

# 2. Publier le programme principal (sortie dans build/release/app/, copie automatique du lanceur vers build/release/Starshot.exe + suppression des bibliothèques IA)
dotnet publish src/Starshot/Starshot.csproj -c Release -p:Platform=x64

# Structure résultante :
# build/release/
#   Starshot.exe        ← Lanceur (copié automatiquement)
#   app/
#     Starshot.exe      ← Programme principal (autonome + trim + R2R)
#     *.dll / avifenc.exe etc.
```

## Internationalisation (i18n)

Les traductions sont basées sur les fichiers `.resx` dans `src/Starshot.Language/` (`Lang.resx` est l'anglais par défaut ; `Lang.zh-CN.resx` etc. par langue). Il faut aussi ajouter une option au ComboBox de langue dans `GeneralSetting` + son mappage `LanguageIndex`.

Contributions de traduction bienvenues : forkez le dépôt → copiez `Lang.resx` en `Lang.{votre-locale}.resx` → traduisez → ouvrez une PR.

## Notes de développement

Ce projet est en phase de développement actif. Les fonctionnalités peuvent évoluer à tout moment — restez à l'écoute des mises à jour !

Contributions bienvenues :

- Vous avez trouvé un bug ? [Soumettre une Issue](../../../issues/new)
- Vous avez une suggestion ? [Lancer une Discussion](../../../issues/new)
- Vous voulez contribuer au code ? Les [Pull Requests](../../../pulls) sont les bienvenues

## FAQ

<details>
<summary><b>Les images de la bibliothèque de captures (page d'accueil) affichent des couleurs incorrectes / brouillées</b></summary>

C'est généralement un problème de codec d'image du système Windows (extensions AVIF / HEIF / JPEG XL), pas un bug de Starshot. Essayez de rechercher et mettre à jour les composants suivants dans le Microsoft Store :

- **AV1 Video Extension**
- **HEIF Image Extensions**
- **HEVC Video Extensions**
- **Webp Image Extensions**

Redémarrez Starshot après la mise à jour. Si le problème persiste, veuillez [soumettre une Issue](../../../issues/new) avec une capture d'écran jointe.

</details>

<details>
<summary><b>Les couleurs de la capture sont différentes de ce que je vois à l'écran</b></summary>

Si vous utilisez un écran HDR, vérifiez que le commutateur HDR de Windows est activé (Paramètres → Système → Affichage → HDR). La fonction de capture HDR ne fonctionne qu'en mode HDR.

</details>

<details>
<summary><b>Je n'arrive pas à coller depuis le presse-papiers après une capture</b></summary>

Starshot utilise l'API native Win32 pour écrire dans le presse-papiers, ce qui est théoriquement plus fiable que WinRT. Si le collage échoue malgré tout, l'application cible ne prend peut-être pas en charge le format correspondant (CF_HDROP pour les fichiers / CF_DIB pour les bitmaps). Essayez de coller dans l'Explorateur (fichiers) ou Paint (bitmaps) pour vérifier.

</details>

## Remerciements

- [Starward](https://github.com/Scighost/Starward) — Le cœur de capture, le moteur de codec et le framework de fenêtre proviennent tous de Starward, développé par [@Scighost](https://github.com/Scighost)
- [ShareX](https://github.com/ShareX/ShareX) — Référence pour la détection de fenêtres et le design d'interaction de l'overlay de capture régionale

**Et toutes les bibliothèques tierces utilisées** :

- [CommunityToolkit](https://github.com/CommunityToolkit) — Framework MVVM + contrôles WinUI (Segmented / Behaviors / Helpers)
- [SharpCompress](https://github.com/adamhathcock/sharpcompress) — Décompression en streaming
- [Dapper](https://github.com/DapperLib/Dapper) — ORM léger pour SQLite
- [H.NotifyIcon.WinUI](https://github.com/HavenDV/H.NotifyIcon) — Barre des tâches système
- [Vanara.PInvoke](https://github.com/dahall/Vanara) — Wrappers d'API Win32 (DwmApi / Ole / Shell32)
- [ComputeSharp.D2D1](https://github.com/Sergio0694/ComputeSharp) — Effets de calcul GPU
- [Serilog](https://github.com/serilog/serilog) — Journalisation structurée

## License

MIT
