<div align="center">

<img src="../src/logo.png" width="120" alt="Starshot ロゴ">

# Starshot

**次世代 Windows ネイティブ HDR スクリーンショットツール**

16bit フルパイプラインキャプチャ · 領域スクリーンショット · AVIF / JPEG XL エンコード · カラーマネジメント

[![Release](https://img.shields.io/github/v/release/loliri/Starshot?style=flat-square)](../../releases)
[![License](https://img.shields.io/badge/license-MIT-blue?style=flat-square)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6?style=flat-square&logo=windows)](../../releases)

[ダウンロード](../../releases) · [クイックスタート](#クイックスタート) · [機能詳細](#機能詳細) · [ソースからビルド](#ソースからビルド)

**[简体中文](../README.md)** | **[English](README.en.md)** | **[繁體中文](README.zh-TW.md)** | **日本語** | **[Français](README.fr.md)** | **[Русский](README.ru.md)** | **[Español](README.es.md)**

</div>

---

## なぜ Starshot が必要か

Windows 標準のスクリーンショットツール（Snipping Tool、Win+Shift+S）は、HDR ディスプレイ上でも 8bit SDR 画像しかキャプチャできません——システムコンポジターが 16bit HDR フレームを圧縮出力し、ハイライトがクリップされ、色域が狭められます。ShareX などの一般的なサードパーティツールも、従来の GDI/BitBlt キャプチャパイプラインに制限され、HDR データを認識できません。

Starshot は、DXGI レイヤーからディスプレイ出力の生の `R16G16B16A16Float` scRGB フレームバッファを直接取得し、HDR 輝度情報（数千 nit まで）を完全に保持し、16bit HDR AVIF または JPEG XL としてエンコードし、BT.2020 + PQ 伝達関数メタデータを埋め込みます。さらに、SDR ディスプレイ自動ダウングレード、領域キャプチャ、マルチフォーマットバッチ変換など、一般的なスクリーンショットツールに期待される機能も備えています。

**主な特徴**

- 🎯 **HDR フルパイプラインロスレス**——キャプチャ、エンコード、カラーマネジメントを 16bit で一貫処理、非可逆トーンマッピングなし
- 🧠 **スマート HDR/SDR 判定**——maxCLL ヒストグラムで真の HDR コンテンツと HDR 形式に包まれた SDR コンテンツを区別
- ✂️ **領域スクリーンショット**——フリーズフレームのマルチディスプレイオーバーレイ、ウィンドウ検出 + 拡大鏡で精密な選択
- 📋 **ネイティブクリップボード**——Win32 ネイティブ API でクリップボードに直接書き込み、WinRT 遅延レンダリングの貼り付け失敗を回避
- 🗂️ **マルチフォーマット対応**——AVIF / JPEG XL / UHDR JPEG / PNG、バッチ変換ツール付き
- 📦 **ポータブル**——解凍するだけですぐ使える、管理者権限不要

<div align="center">
<table>
<tr>
<td align="center" width="50%">

**他のツール**

<img src="https://r2.cialo.site/endfield/3840x2160.dlaa.broken.jpg" width="100%" alt="SDRスクリーンショット、ハイライトがクリップされ色あせている">
</td>
<td align="center" width="50%">

**Starshot（Ultra HDR JPEG）**

<img src="https://r2.cialo.site/endfield/3840x2160.dlaa.uhdr.jpg" width="100%" alt="Starshot Ultra HDR JPEG がゲインマップでハイライトのディテールを完全保持">
</td>
</tr>
</table>
<sub>映像は「アークナイツ：エンドフィールド」より</sub>
</div>
</br>

> [!NOTE]
> GitHub は AVIF レンダリングをサポートしていないため、Ultra HDR JPEG を表示しています。AVIF 原画像は[こちら](https://r2.cialo.site/endfield/3840x2160.dlaa.avif)でご覧いただけます。

SDR ディスプレイでは、Starshot は自動的に標準 SDR キャプチャパスを使用する汎用スクリーンショットツールです。HDR ディスプレイでは、HDR データを完全に保持できる数少ないデスクトップキャプチャソリューションの一つです。

## システム要件

- 最適な体験のために Windows 11 を推奨
- x64 アーキテクチャ
- **HDR キャプチャには HDR ディスプレイが必要**（SDR ディスプレイでは自動的に SDR パスにフォールバック）

## ダウンロード

[Releases](../../releases) からアーカイブをダウンロードし、解凍してルートディレクトリの `Starshot.exe` を実行します（ランチャーが自動的に `app/` 内のメインプログラムを起動します）。インストール不要で、解凍するだけですぐ使えます。

## クイックスタート

| 操作                                                     | デフォルトショートカット |
| -------------------------------------------------------- | ------------------------ |
| 全画面スクリーンショット                                 | Alt+W                    |
| 領域スクリーンショット（選択後ファイル保存 + クリップボードにコピー） | Alt+Q          |
| 領域コピーのみ（クリップボードにコピー、ファイル保存なし） | Alt+A                    |

すべてのショートカットは設定でカスタマイズ可能です。

## 機能詳細

### HDR キャプチャパイプライン

ほとんどのスクリーンショットツールは HDR ディスプレイ上でも 8bit SDR しかキャプチャできません——システムコンポジターの 16bit 浮動小数点 scRGB フレームが SDR に押しつぶされ、ハイライトがクリップされ、色域が狭まります。Starshot は**生の HDR フレームバッファ**をキャプチャします：

1. **HDR キャプチャ**：ディスプレイが HDR を報告した場合、`R16G16B16A16Float` ピクセル形式を要求し、完全な scRGB 浮動小数点データ（輝度は数千 nit まで）を取得
2. **HDR 保存**：16bit AVIF / JPEG XL、BT.2020 色域 + PQ 伝達関数。ハイライトのクリッピングなし、色域の縮小なし
3. **maxCLL 計算**：Win2D ヒストグラムエフェクトで最大コンテンツ輝度を計算し、真の HDR コンテンツと HDR 形式の SDR コンテンツを区別
4. **カラーマネジメント**：ディスプレイ ICC プロファイルを読み取り、実際の色域原色を解析し、出力ファイルの cICP/ICC チャンクに埋め込み。HDR は BT.2020 に強制、SDR は切り替え可能（オン = ICC の実際の色域を読み取り、オフ = BT.709）

#### SDR コンテンツの処理

HDR ディスプレイでは、デスクトップや SDR アプリケーションも HDR 形式（R16G16B16A16Float）でキャプチャされますが、実際のコンテンツ輝度は SDR レベルです。Starshot の処理は以下の通りです：

- **デフォルト**：HDR 形式のまま保存（16bit）、**8bit トーンマッピングなし**、劣化や色ずれを防止
- **SDR コンテンツの HDR 削除**トグル（オプション）：有効にすると maxCLL 閾値を検出し、基準未満の場合は自動的に SDR に変換（ユーザー設定の SDR 保存形式を使用）して HDR ファイルを削除、容量を節約

#### UHDR JPEG フォールバック

HDR スクリーンショットは Ultra HDR JPEG（SDR ベース画像 + HDR ゲインマップ）も同時に保存でき、HDR 非対応のソフトウェアでも正常に表示されます。`Starward.Codec` の `UhdrEncoder` でエンコードします。

#### 領域スクリーンショットの HDR トレードオフ

領域スクリーンショットオーバーレイは、HDR フレームを**意図的に** SDR にトーンマッピングして表示します——WinUI の `CanvasControl` は SDR スワップチェーンを使用するため、scRGB 浮動小数点を直接出力すると色ずれや黒つぶれが発生します。**保存されるファイルは完全な HDR** で、変更されません。選択中のハイライトクリッピングはプレビューのみに影響し、出力には影響しません。

### 3 つのキャプチャモード

| モード           | 対象                                       | クリップボード形式    | ファイル |
| ---------------- | ------------------------------------------ | --------------------- | -------- |
| 全画面           | ディスプレイ全体（前景ウィンドウ/カーソル画面、切替可） | CF_HDROP（ファイル）  | 保存     |
| 領域             | ドラッグ選択 / ウィンドウクリック          | CF_DIB（BGRA ビットマップ） | 保存 |
| 領域コピーのみ   | ドラッグ選択 / ウィンドウクリック          | CF_DIB（BGRA ビットマップ） | 保存なし |

3 つのモードは HDR 検出、カラーマネジメント、ファイル名テンプレート、保存パイプライン、情報トーストを共有します。

### 領域スクリーンショットオーバーレイ

- **フリーズフレーム**：最初に全ディスプレイを 1 枚のビットマップに合成し、オーバーレイにフリーズフレームを表示——選択中も画面は動かず、オーバーレイ自体はスクリーンショットに含まれない
- **マルチディスプレイ**：仮想画面全体をカバー、拡大鏡と座標ボックスはカーソルのあるディスプレイに固定（画面をまたがない）
- **ウィンドウ検出**：EnumWindows + DWM cloaked/toolwindow フィルタリング + DWM 拡張フレーム境界（影の除去） + クライアント領域デュアル候補 + Z オーダー選択、ウィンドウをクリックして直接キャプチャ（QuickCrop）
- **拡大鏡**：NearestNeighbor 整数アライン + ピクセルグリッド（15×15 ピクセル、各 10px）、ピクセルが鮮明に識別可能
- **アニメーション行進アリ + リアルタイム座標**：選択範囲 X/Y/W/H + カーソル物理座標
- **ピクセル精度**：ドラッグ選択 +1px、ウィンドウ矩形 +0
- ESC / 右クリックでキャンセル、Enter でウィンドウホバー確定

### クリップボード

パッケージ化されていない WinUI アプリの WinRT `Clipboard.SetContent` は信頼性が低い（遅延レンダリング + Flush の問題で、コンテンツが他のアプリに届かないことが多い）。Starshot は Win32 ネイティブ API（`OpenClipboard` / `SetClipboardData`）を直接使用します：

- **全画面**：CF_HDROP（ファイルドラッグ＆ドロップ形式）——エクスプローラーやチャットアプリに貼り付けると直接ファイルを取得
- **領域**：CF_DIB（BGRA ビットマップ）——オーバーレイから切り取った SDR ビットマップをそのままクリップボードに配置、ファイル読み込みなし、再エンコードなし、二次トーンマッピングなし
- 任意のスレッドから呼び出し可能、クリップボード占有時に 10×20ms リトライ

### 保存

- **フラット構造**（サブフォルダなし）、デフォルト `ピクチャ\Starshot`、カスタマイズ可能
- **SDR 形式**（PNG / AVIF / JPEG XL、デフォルト PNG）と **HDR 形式**（AVIF / JPEG XL、デフォルト AVIF）を個別に設定
- 品質：中 / 高 / ロスレス
- XMP メタデータ（CreatorTool = Starshot）
- エンコード直列化（SemaphoreSlim）、同時エンコード競合を防止
- **ストレージ統計**：設定ページでスクリーンショット / サムネイルキャッシュ / 壁紙 / ログのディスク使用量を表示、更新とワンクリックキャッシュクリアに対応（孤立した壁紙ファイルも併せてクリーンアップ）

#### 対応フォーマット

| フォーマット | ビット深度            | HDR 対応                     | 用途                     |
| ------------ | --------------------- | ---------------------------- | ------------------------ |
| PNG          | 8bit / 16bit          | HDR 保存可だが互換性低い     | SDR デフォルト、ロスレス |
| AVIF         | 8bit / 10bit / 12bit  | 完全 HDR                     | HDR デフォルト、高圧縮比 |
| JPEG XL      | 8bit / 16bit          | 完全 HDR                     | HDR 代替、可逆圧縮       |
| UHDR JPEG    | 8bit + ゲインマップ   | SDR 互換 HDR フォールバック  | HDR 追加出力             |

### ファイル名テンプレート

全画面と領域スクリーンショットで**独立したテンプレート**を使用します。

| プレースホルダー                                          | 意味                              | 例                  |
| --------------------------------------------------------- | --------------------------------- | ------------------- |
| `{process}`                                               | プロセス名（拡張子なし）          | `explorer`          |
| `{processPath}`                                           | exe ファイル名（拡張子付き）      | `explorer.exe`      |
| `{title}`                                                 | ウィンドウタイトル（トリム + 切り詰め長設定可） | `Genshin Impact`    |
| `{timestamp}`                                             | Unix タイムスタンプ               | `1721234567`        |
| `{time}`                                                  | yyyyMMdd_HHmmssff                 | `20260718_14302512` |
| `{date}`                                                  | yyyyMMdd                          | `20260718`          |
| `{width}` `{height}`                                      | 画像サイズ（px）                  | `1920` `1080`       |
| `{year}` `{month}` `{day}` `{hour}` `{minute}` `{second}` | 時刻の各成分                      |                     |

ファイル名に使用できない文字は一律 `_` に置換されます。

### 情報トースト

スクリーンショット後、サムネイル + ステータストーストがポップアップします（スクリーンショットには影響しません——`WDA_EXCLUDEFROMCAPTURE` が設定されており、他のキャプチャツールはこのウィンドウを認識できません）：

- **処理中**（スピナー）/ **保存済み**（開くボタン付き）/ **コピー済み**（緑のチェック）/ **失敗**
- 連続キャプチャカウンター（例：2/3）
- Composition アニメーション スライドイン/スライドアウト

### スクリーンショットライブラリ

- マルチフォルダブラウジング（デフォルトのスクリーンショットディレクトリ + ユーザー追加フォルダ）
- `FileSystemWatcher` による追加/削除のリアルタイム検知
- 日付グループ化、サムネイル遅延読み込み
- 右クリックメニュー：開く / ファイルをコピー / JPG としてコピー / エクスプローラーで開く / プログラムから開く / 削除
- 複数選択 + ドラッグアウト + バッチ変換エントリ

### 画像ビューア

- ズーム（スライダー / ボタン / マウスホイール / ダブルクリックでフィット）、フルスクリーンモード（F11）
- 前へ / 次へ（矢印キー、マウスホイール、下部サムネイルストリップ）
- ファイルをドラッグ＆ドロップで開く
- 右クリックメニュー：ファイル / パス / 画像をコピー、削除、エクスプローラーで開く、プログラムから開く
- **編集パネル**：HDR / SDR / Auto 表示モード切替、SDR 明るさスライダー（100–500 nit）、画像とディスプレイ情報
- **フォーマット変換**：PNG / AVIF / JPEG XL（SDR ディスプレイ）または UHDR JPEG / AVIF / JPEG XL（HDR ディスプレイ）にエクスポート
- **カラーマネジメント**：ディスプレイ ICC プロファイルと AdvancedColorInfo を読み取り

### バッチフォーマット変換

| 変換方向                            | エンジン                               |
| ----------------------------------- | -------------------------------------- |
| JPG / PNG → AVIF / JXL              | avifenc.exe / cjxl.exe（CLI）          |
| AVIF / JXL → JPG / PNG              | avifdec.exe / djxl.exe（CLI）          |
| JXR / WEBP / HEIC 等 → AVIF / JXL   | プロセス内 ImageSaver（avifEncoderLite） |

### パーソナライゼーション

- **カスタム壁紙**：3 つのモード
  - **指定画像**：画像を 1 枚選択、固定表示
  - **指定動画**：ミュートループ再生、メインウィンドウ非表示時に自動一時停止
  - **フォルダランダム**：起動ごとにフォルダからランダムに 1 つ選択（画像または動画）
  - 壁紙ソースの消失を自動検出し、設定をクリーンアップして壁紙なしにフォールバック + トースト通知
- **アクセントカラー**：
  - **壁紙から自動抽出**（デフォルトオン）：壁紙の主要色をサンプリングしてアプリアクセントカラーに（HSV 彩度ブースト）；動画は最初のフレームのみサンプリングし、色のちらつきを防止
  - **カスタムカラー**：手動カラーピッカーで自動抽出を上書き
- **テーマ**：システム連動 / ライト / ダーク
- **アクリル効果**：壁紙モードでフロストガラスレイヤーまたは壁紙の直接透過を選択可能

### スプラッシュスクリーン

起動時にロゴ + タグラインを表示し、700ms の遅延後 400ms でフェードアウト。初回ウィンドウ表示時のみトリガーされ、トレイからの復帰時には再表示されません。

### システムトレイ

- 左クリックでメインウィンドウを表示、右クリックでコンテキストメニュー（表示 / 終了）
- メインウィンドウを閉じるとトレイに最小化（切り替え可能）
- `ForceExit` メカニズムでトレイの「終了」が確実に終了することを保証

### スタートアップ自動起動

- レジストリ `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`、ランチャー（ルート `Starshot.exe`）を指す
- オプションの `--hide` フラグでトレイに最小化して起動（トレイ有効時）

## アーキテクチャ

### ディレクトリ構造

```
ルート/
  Starshot.exe            ← C++ ランチャー（~400KB、app/ メインプログラムを起動）
  StarshotDatabase.db     ← SQLite 設定データベース
  app/
    Starshot.exe          ← メインプログラム（WinUI 3 / .NET 10）
    *.dll                 ← 依存ライブラリ
    avifenc.exe 等        ← コーデックツール（Starward.Codec NuGet より）
%LOCALAPPDATA%/Starshot/ （デフォルト、設定可能）
  log/                    ← ログ
  cache/                  ← サムネイルキャッシュ
```

### ランチャー

C++ ネイティブプログラム（~400KB）、現在は `app/Starshot.exe` にハードコードされています。将来的に `version.ini` によるバージョン管理ディレクトリ + 古いバージョンの自動クリーンアップ対応を予定。

### 技術スタック

| レイヤー         | 技術                                                                |
| ---------------- | ------------------------------------------------------------------- |
| UI フレームワーク | WinUI 3（Windows App SDK 1.8）                                      |
| ランタイム       | .NET 10                                                             |
| グラフィックス   | Win2D 1.3（D3D11 相互運用、HDR トーンマッピング、ヒストグラムエフェクト） |
| コーデック       | Starward.Codec NuGet（libavif / libjxl / UltraHDR P/Invoke ラッパー） |
| データストレージ | SQLite + Dapper                                                     |
| ロギング         | Serilog                                                             |
| システムトレイ   | H.NotifyIcon.WinUI                                                  |
| サムネイル       | Scighost.WinUI ImageEx + カスタム CachedImage                       |
| 領域オーバーレイ | Win2D CanvasControl（フリーズフレームレンダリング + 選択範囲描画）  |
| クリップボード   | Win32 ネイティブ API（OpenClipboard / SetClipboardData）            |
| ランチャー       | ネイティブ C++（v145 ツールセット、静的 CRT）                      |

### 再入保護

`Interlocked.CompareExchange` グローバルガード——全画面、領域、コピー専用モードで単一の `_isCapturing` フラグを共有し、キーボードの連打や高速な連続ホットキー押下による複数キャプチャを防止します。

### ビルド設定

|                    | Debug                                       | Release                                                                                          |
| ------------------ | ------------------------------------------- | ------------------------------------------------------------------------------------------------ |
| .NET ランタイム    | Framework-dependent（バンドルなし）         | 自己完結型                                                                                       |
| ネイティブライブラリ | win-x64 のみ（RuntimeIdentifier、出力ルートにフラット配置） | Debug と同様                                                                     |
| Trim               | 無効                                        | 部分的                                                                                           |
| ReadyToRun         | 無効                                        | 有効                                                                                             |
| 追加クリーンアップ | —                                           | DirectML.dll / onnxruntime.dll / NpuDetect を削除（Windows App SDK の WinML/AI コンポーネント、本アプリでは未使用） |
| 出力パス           | `build/app/`                                | `build/release/app/` + ランチャーを `build/release/` にコピー                                     |
| サイズ             | ~80MB                                       | より小さい（Trim + AI ライブラリ削除）                                                           |

## ソースからビルド

### 前提条件

- Visual Studio 2022 / 2026（C++ デスクトップ開発、.NET デスクトップ開発を含む）
- .NET 10 SDK
- Windows SDK 10.0.26100

### 手順

```bash
git clone https://github.com/loliri/Starshot
cd Starshot

# === Debug ===
# メインプログラムをビルド（build/app/ に出力）
dotnet build src/Starshot/Starshot.csproj -c Debug -p:Platform=x64

# ランチャーをビルド（build/Starshot.exe に出力、VS の MSBuild が必要）
"C:\Program Files\Microsoft Visual Studio\<バージョン>\Community\MSBuild\Current\Bin\MSBuild.exe" src/Starshot.Launcher/Starshot.Launcher.vcxproj -p:Configuration=Release -p:Platform=x64

# 実行：build/Starshot.exe（ランチャー）または build/app/Starshot.exe（メインプログラム）

# === Release パブリッシュ ===
# 1. 最初にランチャーをビルド（build/Starshot.exe に出力）
"C:\Program Files\Microsoft Visual Studio\<バージョン>\Community\MSBuild\Current\Bin\MSBuild.exe" src/Starshot.Launcher/Starshot.Launcher.vcxproj -p:Configuration=Release -p:Platform=x64

# 2. メインプログラムをパブリッシュ（build/release/app/ に出力、ランチャーを build/release/Starshot.exe に自動コピー + AI ライブラリ削除）
dotnet publish src/Starshot/Starshot.csproj -c Release -p:Platform=x64

# 完成後のディレクトリ構造：
# build/release/
#   Starshot.exe        ← ランチャー（自動コピー）
#   app/
#     Starshot.exe      ← メインプログラム（自己完結型 + Trim + R2R）
#     *.dll / avifenc.exe 等
```

## 既知の制限

- 領域スクリーンショットオーバーレイの HDR フレームは SDR で表示されます（WinUI CanvasControl は SDR スワップチェーンを使用）；保存ファイルには影響しません
- カスタム壁紙は `UniformToFill` でフィルされますが、WinUI のクロップは中央揃えではなく、現在は**左上**揃えです。例えば、狭い（縦向き）壁紙をワイドウィンドウに表示すると、上部のみが表示されます（中央からではなく上部からクロップ）
- 領域スクリーンショットオーバーレイを開いた瞬間、カーソルはシステムデフォルトの形状のままで、**マウスを一度動かすと**十字カーソルが表示されます（WinUI `ProtectedCursor` は既に要素上にある静止ポインタに即座に適用されないため、一度動かしてポインターイベントをトリガーすると正常に動作します）
- バージョン管理 / 自動更新は未実装

## 謝辞

- [Starward](https://github.com/Scighost/Starward) — スクリーンショットコア、コーデックエンジン、ウィンドウフレームワークはすべて Starward に由来し、[@Scighost](https://github.com/Scighost) によって開発されました
- [ShareX](https://github.com/ShareX/ShareX) — 領域スクリーンショットオーバーレイのウィンドウ検出とインタラクションデザインの参考

## ライセンス

MIT
