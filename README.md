# unitypackage-to-vpm

`.unitypackage` を VPM (VRChat Package Manager) 形式のパッケージディレクトリに変換するCLIツールです。

## できること

- `.unitypackage` を展開し、`Assets/` プレフィックスを取り除いて `Packages/<name>/Runtime/` 配下に再配置
- 複数の `.unitypackage` を指定した場合、順番にマージ（共通マテリアルを使用し、その後アバター依存のパッケージを追加するようなアセットを想定しています）
- プレースホルダの `package.json` を生成（`legacyFolders` も付与）
- `.meta` が欠けているファイル/フォルダがあれば、ローカルのUnity Editorをバッチモードで起動して自動生成
- `Assets/` のハードコードパスや `Application.dataPath` 参照、プリコンパイル済み `.dll` の混入を簡易的に検出して警告
- `--previous` で前バージョンの変換済みパッケージを渡すと、GUIDとpackage.jsonの内容を引き継いだアップデート変換ができる

## 使い方

```
unitypackage-to-vpm [--unity-path <path>] [--previous <dir-or-zip>] [--version <semver>] <output-vpm-directory> <input1.unitypackage> [input2.unitypackage ...]
```

例

```
unitypackage-to-vpm ./MyPackage ./Downloads/SomeAsset.unitypackage
```

複数パッケージをまとめて1つのVPMパッケージにする場合

```
unitypackage-to-vpm ./MyPackage ./base.unitypackage ./extra.unitypackage
```

### Unity Editorのパス指定

出力先に `.meta` が欠けているアセットが1つでもあると、Unityをバッチモードで起動して補完します。Unityの実行ファイルは以下の優先順位で探索されます。

1. `--unity-path <path>` / `-u <path>` / `--unity-path=<path>`
2. `UNITY_PATH` 環境変数
3. Unity Hubの標準インストール先を自動探索（Windows/macOSのみ、複数バージョンがあれば最新のものを使用）

Linuxでの自動探索には対応していないので、Linux環境では `--unity-path` か `UNITY_PATH` を明示してください。

対応するUnityが見つからない、またはすべての `.meta` が既に揃っている場合はUnityの起動自体をスキップします。

### アップデートモード（`--previous`）

`.meta` はこのツールが（またはUnityが）後付けで生成するため、通常の変換は毎回GUIDを新規発行します。同じアセットをバージョンアップして再変換すると、以前配布したパッケージとGUIDがずれてしまい、利用者のシーン/プレハブからの参照が壊れることがあります。

`--previous` に前バージョンの変換済みパッケージ（出力ディレクトリ、または `package.json` をルートに含むVPM用の `.zip`）を渡すと、そのGUIDと `package.json` の内容を新しい出力に引き継ぎます。

```
unitypackage-to-vpm --previous ./MyPackage-v1 ./MyPackage-v2 ./NewAsset-v2.unitypackage
```

```
unitypackage-to-vpm --previous ./MyPackage-v1.zip ./MyPackage-v2 ./NewAsset-v2.unitypackage
```

挙動

- 新しい `.unitypackage` に同梱された `.meta`（GUID）が常に優先されます。前バージョンの `.meta` は、変換後にまだ `.meta` が無いパス（`package.json.meta`、`Runtime` フォルダやその配下のフォルダの `.meta`、元パッケージ側で `.meta` が欠けていたアセットなど）にのみ補完的に使われます。
- 前バージョンの `.meta` が持つGUIDが、新パッケージ内の別パスで既に使われている場合はそのGUIDの引き継ぎをスキップし、警告を出します（Unityが新規GUIDを発行します）。
- `package.json` は前バージョンの内容（`name` / `displayName` / `description` / `author` / `vpmDependencies` / 独自に追加したフィールドなど）をそのまま引き継ぎ、`version` のみ更新します。デフォルトではpatchバージョンを1つ上げます（`1.2.3` → `1.2.4`）。前バージョンの `version` が `major.minor.patch` 形式でない場合はエラーになるので、その場合は `--version <semver>` で明示してください。`legacyFolders` は前バージョンのエントリを引き継ぎつつ、今回の変換で判明した内容で上書き・追加します。
- `--version` は `--previous` と併用時のみ有効です。
- 出力ディレクトリに前バージョンには無かったアセットが含まれていた場合（前バージョンにのみ存在したファイル）は引き継がれません。出力ディレクトリは毎回新規作成する想定です。

## ビルド

```
dotnet publish src/UnityPackageToVpm/UnityPackageToVpm.csproj -c Release -r <RID> -o publish/<RID>
```

`-c Release` でビルドするとNativeAOTでコンパイルされます（`osx-arm64` / `osx-x64` / `win-x64` で動作確認済み。Linux向けのビルド/リリースは現時点で未提供）。

## 注意点

- **package.jsonはプレースホルダのまま出力されます。** `version`（`0.1.0`固定）、`author`、`description`、`vpmDependencies` などは空のまま生成されるため、リリース前に必ず自分で編集してください。
    - name: ハイフン区切りの小文字英数字で、VPMのパッケージ名として有効な形式にしてください。
    - version: リリース時には必ずSemVer形式のバージョンに書き換えてください。
    - displayName: VPMのUIに表示される名前です。日本語を含むとUnity Editorから参照できない場合があるので、英数字のみの名前を推奨します。
- **GUID衝突時は破壊的に上書きします。** 複数の`.unitypackage`を渡した場合、同じGUIDのアセットが別パスに存在すると古いファイルは削除されます。意図した挙動か確認してから使ってください。
- **互換性チェックは簡易的な静的検査です。** `Assets/`のハードコードやDLL内の`Application.dataPath`利用などを検出しますが、これは「壊れる可能性がある」ことを知らせるだけで、実際に動くかどうかの保証にはなりません。
- **.metaの自動生成にはUnity Editorが必要になる場合があります。** 入力パッケージの中に`.meta`が欠けたアセットが含まれていると、ローカルにインストールされたUnityを起動して補完します。CI環境などUnityが無い場所で使う場合は、あらかじめ`.meta`が揃った状態のパッケージを使うか、事前にUnityをインストールしてパスを指定してください。
- **Unityの補完に失敗しても、コマンド自体は正常終了扱いになります。** Unity起動が失敗した場合はエラーログを出力先に`unity_meta_gen.log`として保存しますが、コマンドの終了コードはそれとは連動していません。実行後のログ出力（`All assets already had .meta files` / `invoking Unity to generate them` などのメッセージやエラー出力）を確認してください。
- 一時プロジェクトの `Assets` フォルダをシンボリックリンクとして作成するため、シンボリックリンクの作成が制限された環境（一部のWindows設定など）ではUnityによる`.meta`補完が失敗する可能性があります。
