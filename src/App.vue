<template>
  <div class="main">
    <div v-show="init === 0" class="init-loading">
      <span class="fui-Spinner__spinner">
        <span class="fui-Spinner__spinnerTail"></span>
      </span>
    </div>
    <div
      v-show="init === 2 && !dialog"
      class="content"
      :class="{ borderless: PROJECT_CONFIG.windowBorderless }"
    >
      <div class="controls" v-if="PROJECT_CONFIG.windowBorderless">
        <button class="cont-minimize" @click="minimize">
          <IconMinimize />
        </button>
        <button class="cont-close" @click="close">
          <IconClose />
        </button>
      </div>
      <div class="image">
        <img
          v-if="!useDynamicCss"
          :src="imageSource"
          :alt="PROJECT_CONFIG.title"
        />
      </div>
      <div class="right">
        <div class="title">{{ PROJECT_CONFIG.title }}</div>
        <div class="desc">{{ PROJECT_CONFIG.description }}</div>
        <div v-if="step === 1" class="actions">
          <div v-if="!isUpdate && !INSTALLER_CONFIG.is_uninstall" class="lnk">
            <Checkbox v-model="createLnk" />
            {{ t('step1.createShortcut') }}
          </div>
          <div v-if="!isUpdate && !INSTALLER_CONFIG.is_uninstall" class="read">
            <Checkbox v-model="acceptEula" />
            {{ t('step1.eulaRead') }}
            <a> {{ t('step1.eula') }} </a>
          </div>
          <div v-if="INSTALLER_CONFIG.is_uninstall" class="read">
            <Checkbox v-model="deleteUserData" />
            {{ t('step1.deleteUserData') }}
          </div>
          <div class="more">
            <span>
              <template
                v-if="
                  !INSTALLER_CONFIG.is_uninstall &&
                  Array.isArray(PROJECT_CONFIG.source) &&
                  PROJECT_CONFIG.source.length > 1 &&
                  !INSTALLER_CONFIG.embedded_index?.length
                "
              >
                <span>{{ t('common.from') }} </span>
                <a
                  @click="dialog = 'source'"
                  :title="t('step1.clickSwitchSource')"
                >
                  {{
                    PROJECT_CONFIG.source.find((e) => e.uri === selectedSource)
                      ?.name
                  }}<template v-if="installMode === 'mirrorc'"
                    >({{ mirrorcKey ? markedKey : t('step1.noCdk') }})</template
                  >
                  <IconEdit />
                </a>
              </template>
              <span v-if="!isUpdate && !INSTALLER_CONFIG.is_uninstall">
                {{ t('common.installTo') }}
              </span>
              <span v-if="isUpdate && !INSTALLER_CONFIG.is_uninstall">
                {{ t('common.updateTo') }}
              </span>
              <span v-if="INSTALLER_CONFIG.is_uninstall">
                {{ t('common.uninstallFrom') }}
              </span>
            </span>
            <a
              v-if="!INSTALLER_CONFIG.is_uninstall"
              @click="changeSource"
              :title="t('step1.clickChangePath')"
              >{{ source }}<IconEdit
            /></a>
            <a v-else>{{ source }}</a>
          </div>
          <button
            v-if="!INSTALLER_CONFIG.is_uninstall"
            class="btn btn-install"
            @click="install"
            :disabled="!isUpdate && !acceptEula"
          >
            <IconSheild
              style="
                width: 20px;
                margin-right: 6px;
                margin-left: -6px;
                padding-top: 2px;
              "
              v-if="needElevate || INSTALLER_CONFIG.elevated"
            />
            {{ isUpdate ? t('common.update') : t('common.install') }}
          </button>
          <button
            v-if="INSTALLER_CONFIG.is_uninstall"
            class="btn btn-install"
            @click="uninstall"
          >
            <IconSheild
              style="
                width: 20px;
                margin-right: 6px;
                margin-left: -6px;
                padding-top: 2px;
              "
              v-if="needElevate || INSTALLER_CONFIG.elevated"
            />
            {{ t('common.uninstall') }}
          </button>
        </div>
        <div class="progress" v-if="step === 2">
          <div class="step-desc">
            <div
              v-for="(i, a) in installMode === 'mirrorc'
                ? subStepListMirrorc
                : subStepList"
              class="substep"
              :class="{ done: a < subStep }"
              v-show="a <= subStep"
              :key="i"
            >
              <span v-if="a === subStep" class="fui-Spinner__spinner">
                <span class="fui-Spinner__spinnerTail"></span>
              </span>
              <span v-else class="substep-done">
                <CircleSuccess />
              </span>
              <div>{{ i }}</div>
            </div>
          </div>
          <div class="current-status" v-html="current"></div>
          <div class="progress-bar" :style="{ width: `${percent}%` }"></div>
        </div>
        <div class="finish" v-if="step === 3">
          <div class="finish-text">
            <CircleSuccess />
            {{ isUpdate ? t('finish.updated') : t('finish.installed') }}
          </div>
          <button class="btn btn-install" @click="launch">
            {{ t('common.launch') }}
          </button>
        </div>
        <div class="finish" v-if="step === 4">
          <div class="finish-text">
            <CircleSuccess />
            {{ t('finish.alreadyLatest') }}
          </div>
          <button class="btn btn-install" @click="launch">
            {{ t('common.launch') }}
          </button>
        </div>
        <div class="uninstall" v-if="step === 5">
          <button class="btn btn-install" disabled>
            <span
              class="fui-Spinner__spinner"
              style="width: 16px; height: 16px; margin-right: 8px"
            >
              <span class="fui-Spinner__spinnerTail"></span>
            </span>
            {{ t('finish.uninstalling') }}
          </button>
        </div>
        <div class="finish" v-if="step === 6">
          <div class="finish-text">
            <CircleSuccess />
            {{ t('finish.uninstallDone') }}
          </div>
          <button class="btn btn-install" @click="exit">
            {{ t('common.close') }}
          </button>
        </div>
      </div>
    </div>
    <Dialog v-show="dialog === 'source'" @keydown="handleKeyDown">
      <template #title>
        <div class="title">{{ t('dialog.selectSource') }}</div>
      </template>
      <template #desc>
        <div class="desc">
          {{ t('dialog.selectSourceDesc', { title: PROJECT_CONFIG.title }) }}
        </div>
      </template>
      <template #body v-if="Array.isArray(PROJECT_CONFIG.source)">
        <div class="card-container">
          <template v-for="i in PROJECT_CONFIG.source">
            <div
              class="card"
              v-if="
                !i.hidden ||
                showHiddenSources ||
                INSTALLER_CONFIG.args.source === i.id
              "
              :key="i.id"
              :class="{ active: i.uri === selectedSource }"
              @click="changeSelectedSource(i.uri)"
            >
              <SafeIcon
                :svg-content="i.icon"
                :fallback-component="getDefaultIconComponent(i.uri)"
              />
              <span>{{ i.name }}</span>
            </div>
          </template>
        </div>
      </template>
    </Dialog>
    <Dialog v-show="dialog === 'mirrorc'">
      <template #title>
        <div class="title">{{ t('dialog.mirrorcTitle') }}</div>
      </template>
      <template #desc>
        <div class="desc" v-html="t('dialog.mirrorcDesc')"></div>
      </template>
      <template #body>
        <FInput
          class="cdk-input"
          v-model="mirrorcTempKey"
          type="text"
          :placeholder="t('dialog.mirrorcPlaceholder')"
        />
        <div class="desc">
          <a style="cursor: pointer" @click="openMirrorc">{{
            t('dialog.getCdk')
          }}</a>
        </div>
      </template>
      <template #footer>
        <button
          class="btn btn-install btn-install-2rd neutral"
          @click="dialog = ''"
        >
          {{ t('common.cancel') }}
        </button>
        <button
          class="btn btn-install"
          :disabled="mirrorcChecking"
          @click="changeMirrorcKey"
        >
          <span
            v-if="mirrorcChecking"
            class="fui-Spinner__spinner"
            style="width: 16px; height: 16px; margin-right: 8px"
          >
            <span class="fui-Spinner__spinnerTail"></span>
          </span>
          {{ t('common.confirm') }}
        </button>
      </template>
    </Dialog>
    <component :is="'style'" v-if="useDynamicCss">{{ dynamicCss }}</component>
  </div>
</template>

<style scoped>
.main {
  min-height: 100vh;
  app-region: drag;
}
.init-loading {
  height: 100vh;
  display: flex;
  justify-content: center;
  align-items: center;
  padding-bottom: 24px;
  box-sizing: border-box;
}

.init-loading .fui-Spinner__spinner {
  width: 40px;
  height: 40px;
  --fui-Spinner--strokeWidth: 4px;
}
.content {
  display: flex;
  min-height: 100vh;
  line-height: 1.1;
  text-align: center;
  justify-content: center;
  user-select: none;
  padding: 0 16px;
  gap: 8px;
}

.desc {
  font-size: 14px;
  opacity: 0.8;
  padding-left: 10px;
  padding-bottom: 2px;
}

.image {
  min-width: 180px;
  width: 180px;
  box-sizing: border-box;
  padding: 12px 0 12px 12px;

  img {
    width: 100%;
    height: 100%;
    object-fit: contain;
  }
}

.right {
  position: relative;
  width: calc(100% - 188px);
  text-align: left;
  display: flex;
  flex-direction: column;
  padding: 16px;
  box-sizing: border-box;
  overflow: hidden;
  .borderless & {
    padding-top: 44px;
  }
}

.title {
  font-size: 25px;
  padding: 2px 10px 6px;
}

.btn-install {
  app-region: no-drag;
  height: 40px;
  width: 140px;
  position: absolute;
  bottom: 20px;
  right: 8px;
  &.btn-install-2rd {
    right: 158px;
    width: 100px;
  }
}

.actions {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding-top: 16px;
  app-region: no-drag;
}

.read,
.lnk {
  align-items: center;
  gap: 4px;
  padding-left: 12px;
  font-size: 13px;
  display: flex;

  a {
    cursor: pointer;
  }
}

.more {
  align-items: flex-start;
  gap: 6px;
  padding-top: 8px;
  padding-left: 10px;
  font-size: 13px;
  display: flex;
  flex-direction: column;
  svg {
    width: 12px;
    position: relative;
    top: 2px;
    padding-left: 2px;
    opacity: 0.8;
  }

  span {
    span {
      opacity: 0.8;
    }
  }

  a {
    cursor: pointer;
    font-family:
      Consolas,
      'Courier New',
      Microsoft Yahei;
    opacity: 0.8;
    font-size: 12px;
  }
}

.finish-text {
  text-align: center;
  opacity: 0.9;
  width: 100%;
  padding: 38px 10px;
  font-size: 18px;
  display: flex;
  justify-content: center;
  gap: 8px;
  align-items: center;

  svg {
    width: 24px;
  }
}

.progress-bar {
  position: fixed;
  bottom: 0;
  left: 0;
  height: 4px;
  background: var(--colorBrandForeground1);
  transition: width 0.1s;
  transition-timing-function: cubic-bezier(0.33, 0, 0.67, 1); /* easeInOut */
  width: 30%;
}

.step-desc {
  padding: 14px 10px;
  font-size: 14px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.substep {
  display: flex;
  gap: 6px;

  .fui-Spinner__spinner {
    width: 16px;
    height: 16px;
    display: block;
  }

  .substep-done {
    width: 16px;
    height: 16px;
    display: block;
  }
}

.substep.done {
  font-size: 13px;
  opacity: 0.8;
}

.current-status {
  position: relative;
  max-width: 100%;
  font-size: 12px;
  opacity: 0.7;
  padding-left: 14px;
  margin-top: -6px;
  font-family:
    Consolas,
    'Courier New',
    Microsoft Yahei;
}
.uninstall {
  height: 117px;
  display: flex;
  justify-content: center;
  align-items: center;
}

.uninstall .fui-Spinner__spinner {
  width: 40px;
  height: 40px;
  display: block;
  --fui-Spinner--strokeWidth: 4px;
}
</style>
<style>
.d-single-stat {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.d-single-list {
  display: flex;
  flex-direction: column;
  height: 55px;
  overflow: hidden;
  padding-top: 4px;
  font-size: 11px;
  gap: 2px;
  width: 230px;
  max-height: 250px;
  overflow-y: auto;
  padding-left: 20px;

  &::-webkit-scrollbar {
    width: 4px;
  }

  &::-webkit-scrollbar-thumb {
    background: var(--colorBrandForeground1);
    border-radius: 4px;
  }

  &::-webkit-scrollbar-track {
    background: var(--colorBrandBackground);
  }

  &::-webkit-scrollbar-thumb:hover {
    background: var(--colorBrandForeground2);
  }
}

.d-single {
  display: flex;
  justify-content: space-between;
  gap: 8px;
}

.d-single-filename {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
}

.d-single-progress {
  width: 36px;
  min-width: 36px;
}
.cdk-input {
  app-region: no-drag;
  margin: 30px 10px;
  margin-bottom: 48px;
  width: 320px;
  input {
    font-family: Consolas, monospace !important;
  }
}
.card {
  padding: 8px 10px;
  font-size: 12px;
  opacity: 0.6;
  border: 1px solid #fff;
  border-radius: 5px;
  width: 74px;
  height: 74px;
  text-align: center;
  display: flex;
  flex-direction: column;
  justify-content: space-evenly;
  align-items: center;
  cursor: pointer;
  transition: all 0.1s ease-in-out;
  &:hover {
    opacity: 1;
  }
  &.active {
    background: rgba(255, 255, 255, 0.1);
    opacity: 1;
  }
}

.card-container {
  padding: 8px 10px;
  display: flex;
  gap: 18px;
  justify-content: center;
  align-items: center;
  height: 150px;
  app-region: no-drag;
}

.card svg {
  width: 40px;
}

.controls {
  app-region: no-drag;
  position: absolute;
  right: 0;
  top: 0;
  z-index: 9999;
  height: 32px;
  display: flex;
  & > button {
    width: 45px;
    height: 32px;
    overflow: hidden;
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
    svg {
      height: 14px;
    }
    appearance: none;
    background: transparent;
    border: 0;
    color: inherit;
    &:active {
      opacity: 0.8;
    }
  }
  .cont-close:hover {
    background: #c42b1c;
  }

  .cont-minimize:hover {
    background: rgba(255, 255, 255, 0.07);
  }
}
</style>
<script lang="ts" setup>
import { computed, onMounted, onUnmounted, reactive, ref, watch } from 'vue';
import Checkbox from './Checkbox.vue';
import CircleSuccess from './CircleSuccess.vue';
import IconEdit from './IconEdit.vue';
import { getCurrentWindow, invoke, sep } from './tauri';
import {
  getDfsMetadata,
  cleanupAllDfs2Sessions,
  collectDfs2Ranges,
  dfsIndexCache,
  createDfs2Session,
  preprocessFiles,
  getFileInstallMode,
} from './dfs';
import { pluginManager } from './plugins';
import { networkInsights } from './networkInsights';
import {
  DownloadTaskManager,
  SingleFileTask,
  LocalFileTask,
  MergedGroupTask,
  type DownloadContext,
} from './downloadTaskManager';
import {
  error,
  ipcCheckLocalFiles,
  ipcCreateLnk,
  ipcCreateUninstaller,
  ipcFindProcessByName,
  ipcInstallRuntime,
  ipcIsFolderEmpty,
  ipcKillProcess,
  ipcRmList,
  ipcRunMirrorcDownload,
  ipcRunMirrorcInstall,
  ipcRunUninstall,
  ipcWriteRegistry,
  ipPrepare,
  log,
  MirrorcUpdate,
  sendInsight,
  warn,
} from './api/ipc';
import IconSheild from './IconSheild.vue';
import { getRuntimeName } from './consts';
import Dialog from './Dialog.vue';
import Cloud from './Cloud.vue';
import CloudPaid from './CloudPaid.vue';
import Feedback from './Feedback.vue';
import SafeIcon from './components/SafeIcon.vue';
import FInput from './FInput.vue';
import { compare } from 'compare-versions';
import { processMirrorcError } from './mirrorc-errors';
import {
  DfsMetadataHashInfo,
  DfsMetadataHashType,
  DfsUpdateTask,
  InstallerConfig,
  InstallStat,
  InvokeGetDfsMetadataRes,
  InvokeGetDirsRes,
  InvokeSelectDirRes,
  ProjectConfig,
  VirtualMergedFile,
} from './types.ts';
import IconMinimize from './IconMinimize.vue';
import IconClose from './IconClose.vue';
import { setLanguage, t, tFor } from './i18n';

const init = ref(0);

const subStepList = computed<ReadonlyArray<string>>(() => [
  t('substep.getLatest'),
  t('substep.verify'),
  t('substep.download'),
  t('substep.env'),
]);
const subStepListMirrorc = computed<ReadonlyArray<string>>(() => [
  t('substep.mirrorcGetLatest'),
  t('substep.mirrorcDownload'),
  t('substep.mirrorcExtract'),
  t('substep.env'),
]);

const isUpdate = ref<boolean>(false);
const acceptEula = ref<boolean>(true);
const createLnk = ref<boolean>(true);
const deleteUserData = ref<boolean>(false);
const step = ref<number>(1);
const subStep = ref<number>(0);
const needElevate = ref(true);

const current = ref<string>('');
const percent = ref<number>(0);
const source = ref<string>('');
const progressInterval = ref<number>(0);

const dialog = ref<'' | 'mirrorc' | 'source'>('');

// Dynamic image/CSS state
const imageSource = ref<string>('');
const dynamicCss = ref<string>('');
const useDynamicCss = ref<boolean>(false);

// Hidden sources easter egg state
const commaCount = ref<number>(0);
const showHiddenSources = ref<boolean>(false);
const commaTimeout = ref<number>(0);

const selectedSource = ref<string>('');
const installMode = computed<'default' | 'mirrorc'>(() => {
  if (selectedSource.value.startsWith('mirrorc://')) {
    return 'mirrorc';
  } else {
    return 'default';
  }
});
const mirrorcKey = ref<string>('');
const markedKey = computed(() => {
  return (
    mirrorcKey.value.substring(0, 4) +
    '****' +
    mirrorcKey.value.substring(mirrorcKey.value.length - 4)
  );
});

const getDefaultIconComponent = (uri: string) => {
  if (uri.includes('=beta')) return Feedback;
  if (uri.startsWith('mirrorc://')) return CloudPaid;
  return Cloud;
};
watch(
  () => installMode.value,
  async (newValue) => {
    if (newValue === 'mirrorc' && !mirrorcKey.value) {
      try {
        mirrorcKey.value = await invoke('wincred_read', {
          target: `KachinaInstaller_MirrorChyanCDK_${PROJECT_CONFIG.appName}`,
        });
      } catch (e) {}
    }
  },
);

// Watch dialog state changes to reset hidden sources state
watch(
  () => dialog.value,
  (newValue, oldValue) => {
    // Reset hidden sources state when leaving the source dialog
    if (oldValue === 'source' && newValue !== 'source') {
      resetHiddenSourcesState();
    }
  },
);

const PROJECT_CONFIG: ProjectConfig = reactive({
  source: '',
  appName: 'Kachina',
  publisher: 'YuehaiTeam',
  regName: 'Kachina',
  exeName: 'inst.exe',
  uninstallName: 'uninst.exe',
  updaterName: 'update.exe',
  programFilesPath: 'Kachina',
  userDataPath: [],
  ignoreFolderPath: [],
  extraUninstallPath: [],
  title: 'Title',
  description: 'description',
  windowTitle: ' ',
  uacStrategy: 'prefer-admin',
  language: 'auto',
  windowBorderless: false,
});

const INSTALLER_CONFIG: InstallerConfig = reactive({
  install_path: '',
  install_path_exists: false,
  install_path_source: 'DEFAULT',
  is_uninstall: false,
  embedded_config: null,
  enbedded_metadata: null,
  embedded_image: null,
  embedded_files: [],
  embedded_index: [],
  exe_path: '',
  args: {
    target: null,
    uninstall: false,
    non_interactive: false,
    silent: false,
    online: false,
  },
  elevated: false,
});

const getInsightBase = () => {
  const qs = new URLSearchParams();
  if (INSTALLER_CONFIG.args.non_interactive) {
    qs.set('non_interactive', '1');
  }
  if (INSTALLER_CONFIG.args.silent) {
    qs.set('silent', '1');
  }
  if (INSTALLER_CONFIG.args.uninstall) {
    qs.set('uninstall', '1');
  }
  if (INSTALLER_CONFIG.args.online) {
    qs.set('online', '1');
  }
  if ((INSTALLER_CONFIG.embedded_index?.length || 0) > 0) {
    qs.set('pack', '1');
  }
  return `/${PROJECT_CONFIG.appName}?${qs.toString()}`;
};

async function getSource(scan: boolean): Promise<InstallerConfig> {
  return await invoke<InstallerConfig>('get_installer_config', {
    scanExe: scan,
  });
}

function getSourceId(): string {
  if (Array.isArray(PROJECT_CONFIG.source)) {
    const sourceItem = PROJECT_CONFIG.source.find(
      (s) => s.uri === selectedSource.value,
    );
    return sourceItem?.id || 'unknown';
  } else {
    return 'default';
  }
}

function buildEventString(
  version: string,
  useOnlineSource: boolean = false,
): string {
  const action = isUpdate.value ? 'update' : 'install';
  const isPackedMode = (INSTALLER_CONFIG.embedded_index?.length || 0) > 0;

  if (isPackedMode) {
    if (useOnlineSource) {
      return `${action}/packed+${getSourceId()}/${version}`;
    } else {
      return `${action}/packed/${version}`;
    }
  } else {
    return `${action}/${getSourceId()}/${version}`;
  }
}

async function installPrepare(
  version: string,
  useOnlineSource: boolean = false,
): Promise<boolean> {
  await ipPrepare(needElevate.value);
  sendInsight(getInsightBase(), buildEventString(version, useOnlineSource));
  const target_exe_path = `${source.value}${sep()}${PROJECT_CONFIG.exeName}`;
  const runningExes =
    (await ipcFindProcessByName(PROJECT_CONFIG.exeName).catch(log)) || [];
  if (
    runningExes.find(
      (e) =>
        e[1].toLowerCase().replace(/\\/g, '/') ===
        target_exe_path.toLowerCase().replace(/\\/g, '/'),
    )
  ) {
    const result =
      INSTALLER_CONFIG.args.non_interactive ||
      INSTALLER_CONFIG.args.silent ||
      (await confirm(
        t('confirm.appRunning', { appName: PROJECT_CONFIG.appName }),
        t('common.notice'),
      ));
    if (!result) {
      step.value = 1;
      return false;
    } else {
      try {
        try {
          await Promise.all(
            runningExes.map((e) => ipcKillProcess(e[0], needElevate.value)),
          );
        } catch (e) {
          await Promise.all(runningExes.map((e) => ipcKillProcess(e[0], true)));
        }
        return true;
      } catch (e) {
        error(e);
        await dialog_error(
          t('err.killFailed', { err: String(e) }),
          t('common.error'),
        );
        step.value = 1;
        return false;
      }
    }
  }
  return false;
}

async function installRuntimes() {
  if (PROJECT_CONFIG.runtimes) {
    log('latest_meta.runtimes', PROJECT_CONFIG.runtimes);
    subStep.value = 3;
    current.value = t('status.runtimeInstalling');
    for (const tag of PROJECT_CONFIG.runtimes) {
      log(`Installing runtime: ${tag}`);
      current.value = t('status.runtimeInstallName', {
        name: getRuntimeName(tag),
      });
      const tryTimes = 3;
      const embedRuntime = INSTALLER_CONFIG.embedded_files?.find(
        (e) => e.name === tag,
      );
      for (let i = 0; i < tryTimes; i++) {
        try {
          await ipcInstallRuntime(
            tag,
            embedRuntime?.offset,
            embedRuntime?.size,
            ({ payload }) => {
              const currentSize = formatSize(payload[0]);
              const targetSize = payload[1] ? formatSize(payload[1]) : '';
              if (payload[0] >= payload[1] - 1) {
                current.value = t('status.runtimeInstallName', {
                  name: getRuntimeName(tag),
                });
              } else {
                current.value = `${t('status.runtimeDownloadName', { name: getRuntimeName(tag) })}<br>${currentSize}${targetSize ? ` / ${targetSize}` : ''}`;
              }
            },
            needElevate.value,
          );
          break;
        } catch (e) {
          if (i === tryTimes - 1) {
            error(e);
            await dialog_error(
              t('err.runtimeInstallFailed', {
                name: getRuntimeName(tag),
                err: String(e),
              }),
              t('common.error'),
            );
            break;
          } else {
            log(`安装${getRuntimeName(tag)}失败: ${e}，重试中`);
          }
        }
      }
    }
  }
}

async function runInstall(): Promise<void> {
  step.value = 2;
  let latest_meta = INSTALLER_CONFIG.enbedded_metadata;
  let online_meta: InvokeGetDfsMetadataRes | null = null;
  let online_meta_err = '';
  try {
    online_meta = await getDfsMetadata(
      selectedSource.value,
      INSTALLER_CONFIG.args.dfs_extras,
    );
  } catch (e) {
    online_meta_err = error(e);
  }
  let meta_tag = '';
  if (!latest_meta && !online_meta) {
    await dialog_error(
      t('err.getMetaFailed') +
        (online_meta_err ? `\n${online_meta_err}` : t('err.unknownCheckLog')),
      t('common.error'),
    );
    step.value = 1;
    return;
  } else if (!latest_meta) {
    latest_meta = online_meta;
    log('Local meta not found, use online meta');
  } else if (
    online_meta &&
    online_meta.tag_name !== latest_meta.tag_name &&
    compare(online_meta.tag_name, latest_meta.tag_name, '>')
  ) {
    log('Version update detected');
    if (
      !INSTALLER_CONFIG.args.non_interactive &&
      !INSTALLER_CONFIG.args.silent &&
      ((isUpdate.value &&
        (INSTALLER_CONFIG.embedded_index?.length || 0) <= 0) ||
        (await confirm(t('confirm.notLatest'))))
    ) {
      meta_tag = latest_meta.tag_name;
      latest_meta = online_meta;
    } else {
      log('Has version update but use local meta');
    }
  } else {
    log('Local meta found, use local meta');
  }
  latest_meta = latest_meta as InvokeGetDfsMetadataRes;
  if (
    isUpdate.value &&
    latest_meta.installer &&
    !INSTALLER_CONFIG.enbedded_metadata
  ) {
    if (
      !latest_meta.hashed.find(
        (e) => e.file_name === PROJECT_CONFIG.updaterName,
      )
    ) {
      const installerMeta: DfsMetadataHashInfo = {
        file_name: PROJECT_CONFIG.updaterName,
        size: latest_meta.installer.size,
        md5: latest_meta.installer.md5,
        xxh: latest_meta.installer.xxh,
        installer: true,
      };
      latest_meta.hashed.push(installerMeta);
    }
  }
  const useOnlineSource = latest_meta !== INSTALLER_CONFIG.enbedded_metadata;
  if (await installPrepare(latest_meta?.tag_name, useOnlineSource))
    return runInstall();
  let hashKey = '';
  if (latest_meta.hashed.every((e) => e.md5)) {
    hashKey = 'md5';
  } else if (latest_meta.hashed.every((e) => e.xxh)) {
    hashKey = 'xxh';
  } else {
    throw new Error(t('err.unsupportedHash'));
  }
  subStep.value = 1;
  percent.value = 5;
  const local_meta = (
    await ipcCheckLocalFiles(
      {
        source: source.value,
        hash_algorithm: hashKey,
        file_list: latest_meta.hashed.map((e) => e.file_name),
      },
      ({ payload }) => {
        const [currentValue, total] = payload;
        current.value = `${currentValue} / ${total}`;
        percent.value = 5 + (currentValue / total) * 15;
      },
      needElevate.value,
    )
  ).map((e) => {
    return {
      ...e,
      file_name: e.file_name.replace(source.value, ''),
    };
  });
  current.value = t('status.verifyLocal');
  const diff_files: Array<DfsUpdateTask> = [];
  const strip_first_slash = (s: string) => {
    let ss = s.replace(/\\/g, '/');
    if (ss.startsWith('/')) return ss.slice(1);
    return ss;
  };
  const userDataPath = PROJECT_CONFIG.userDataPath.map(replacePathEnvirables);
  const ignoreFolderPath = PROJECT_CONFIG.ignoreFolderPath || [];

  // 预先检查所有 ignoreFolderPath 是否非空（仅在更新场景下检查）
  const ignoreMap: string[] = [];
  if (isUpdate.value && ignoreFolderPath.length > 0) {
    for (const folder of ignoreFolderPath) {
      try {
        const fullPath = replacePathEnvirables(folder).replace(
          /[\\\/]+/g,
          sep(),
        );
        const [isEmpty] = await ipcIsFolderEmpty(fullPath);
        if (!isEmpty) {
          ignoreMap.push(fullPath.toLowerCase().replace(/[\\\/]+/g, sep()));
        }
      } catch (e) {
        // 预检查失败不阻塞安装，仅记录警告并跳过该规则
        warn(`ignoreFolderPath 检查失败 (${folder}), 将跳过该规则:`, e);
      }
    }
  }
  for (const item of latest_meta.hashed) {
    const local = local_meta.find(
      (e: { file_name: string }) =>
        strip_first_slash(e.file_name.toLowerCase()) ===
        strip_first_slash(item.file_name.toLowerCase()),
    );
    if (
      local &&
      userDataPath.some((userData) =>
        strip_first_slash(local.file_name)
          .toLowerCase()
          .startsWith(strip_first_slash(userData).toLowerCase()),
      )
    ) {
      continue;
    }

    // 新增的 ignoreFolderPath 检查
    // 关键：必须是更新场景 + 文件夹非空才跳过
    if (isUpdate.value && ignoreFolderPath.length > 0) {
      const itemCheckFullPath = `${source.value}${sep()}${item.file_name}`
        .toLowerCase()
        .replace(/[\\\/]+/g, sep());
      if (
        ignoreMap.some((ignoreFolder) => {
          return itemCheckFullPath.startsWith(ignoreFolder);
        })
      ) {
        continue;
      }
    }
    if (!local || local.hash !== item[hashKey as DfsMetadataHashType]) {
      let patch = latest_meta.patches?.find(
        (e) =>
          e.from[hashKey as DfsMetadataHashType] === local?.hash &&
          e.to[hashKey as DfsMetadataHashType] ===
            item[hashKey as DfsMetadataHashType],
      );
      let lpatch = latest_meta.patches?.find((e) =>
        INSTALLER_CONFIG.embedded_files?.some(
          (em) => em.name === e.from[hashKey as DfsMetadataHashType],
        ),
      );
      diff_files.push({
        ...item,
        patch,
        lpatch,
        downloaded: 0,
        running: false,
        old_hash: local?.hash,
        unwritable: local?.unwritable || false,
      });
    }
  }
  if (diff_files.length === 0) {
    await finishInstall(latest_meta);
    percent.value = 100;
    step.value = 4;
    return;
  }
  if (
    diff_files.find(
      (e) => e.unwritable && e.file_name !== PROJECT_CONFIG.updaterName,
    )
  ) {
    if (
      !INSTALLER_CONFIG.args.non_interactive &&
      !INSTALLER_CONFIG.args.silent &&
      !(await confirm(
        t('confirm.filesInUse') +
          diff_files
            .filter(
              (e) => e.unwritable && e.file_name !== PROJECT_CONFIG.updaterName,
            )
            .map((e) => e.file_name)
            .join('\n'),
      ))
    ) {
      step.value = 1;
      return;
    }
  }
  console.log('Files to install:', diff_files);

  // Create DFS2 session if using DFS2 source
  if (selectedSource.value.startsWith('dfs2+')) {
    current.value = t('status.creatingSession');
    try {
      const ranges = collectDfs2Ranges(
        diff_files,
        INSTALLER_CONFIG.embedded_files || [],
        selectedSource.value,
        hashKey as DfsMetadataHashType,
      );

      if (ranges.length > 0) {
        const apiUrl = selectedSource.value.replace(/^dfs2\+packed\+/, '');

        // Get resource version from cache
        const cache = dfsIndexCache.get(selectedSource.value);
        const resourceVersion = cache?.resource_version;

        const sessionId = await createDfs2Session(
          apiUrl,
          ranges,
          resourceVersion, // Use specific version from metadata
          INSTALLER_CONFIG.args.dfs_extras || undefined,
        );

        log('DFS2 session created successfully:', sessionId);
      }
    } catch (e) {
      error('Failed to create DFS2 session:', e);
      await dialog_error(t('err.createSessionFailed', { err: String(e) }));
      step.value = 1;
      return;
    }
  }

  // 插件会话创建
  const plugin = pluginManager.findPlugin(selectedSource.value);
  if (plugin?.createSession) {
    try {
      const ranges = collectDfs2Ranges(
        diff_files,
        INSTALLER_CONFIG.embedded_files || [],
        selectedSource.value,
        hashKey as DfsMetadataHashType,
      );

      if (ranges.length > 0) {
        const cleanUrl = pluginManager.getCleanUrl(selectedSource.value);
        if (!cleanUrl)
          throw new Error('Invalid plugin URL: ' + selectedSource.value);
        const sessionId = await plugin.createSession(cleanUrl, ranges);
        log('Plugin session created:', sessionId);
      }
    } catch (e) {
      error('Failed to create plugin session:', e);
      await dialog_error(t('err.createSessionFailed', { err: String(e) }));
      step.value = 1;
      return;
    }
  }

  subStep.value = 2;
  current.value = t('status.prepareDownload');

  // 预处理文件，进行合并分组
  const { processedFiles } = preprocessFiles(
    diff_files,
    selectedSource.value,
    hashKey as DfsMetadataHashType,
    INSTALLER_CONFIG.embedded_files || [],
  );

  let stat: InstallStat = {
    speedLastSize: 0,
    lastTime: performance.now(),
    speed: 0,
  };
  progressInterval.value = setInterval(() => {
    // 更新虚拟文件的状态
    processedFiles.forEach((item) => {
      if ((item as VirtualMergedFile)._isMergedGroup) {
        const virtualFile = item as VirtualMergedFile;
        // 计算虚拟文件的总下载量（所有内部文件的下载量之和）
        virtualFile.downloaded = virtualFile._mergedInfo.files.reduce(
          (sum, f) => sum + f.downloaded,
          0,
        );
        // 更新虚拟文件的运行状态（任意内部文件运行中则虚拟文件运行中）
        virtualFile.running = virtualFile._mergedInfo.files.some(
          (f) => f.running,
        );
      }
      // 单文件无需处理，因为runDfsDownload直接更新了对象
    });

    // 计算总大小和已下载大小，直接使用processedFiles
    const total_size = processedFiles.reduce((acc, cur) => {
      if ((cur as VirtualMergedFile)._isMergedGroup) {
        const virtualFile = cur as VirtualMergedFile;
        // 使用实际文件大小总和，不是合并下载大小
        return (
          acc +
          virtualFile._mergedInfo.files.reduce(
            (sum, f) =>
              sum +
              ((!f.failed && (f?.patch?.size || f?.lpatch?.size)) || f.size),
            0,
          )
        );
      } else {
        const file = cur as DfsUpdateTask;
        return (
          acc +
          ((!file.failed && (file?.patch?.size || file?.lpatch?.size)) ||
            file.size)
        );
      }
    }, 0);

    const now = performance.now();
    const time_diff = now - stat.lastTime;
    const downloadedTotalSize = processedFiles.reduce((acc, cur) => {
      if ((cur as VirtualMergedFile)._isMergedGroup) {
        const virtualFile = cur as VirtualMergedFile;
        return (
          acc +
          virtualFile._mergedInfo.files.reduce(
            (sum, f) => sum + f.downloaded,
            0,
          )
        );
      } else {
        return acc + (cur as DfsUpdateTask).downloaded;
      }
    }, 0);
    if (time_diff > 100) {
      stat.speed = (downloadedTotalSize - stat.speedLastSize) / time_diff;
      stat.speedLastSize = downloadedTotalSize;
      stat.lastTime = now;
    }
    const speed = formatSize(stat.speed * 1000);
    const downloaded = formatSize(downloadedTotalSize);
    const total = formatSize(total_size);

    // 更新运行中任务显示逻辑
    const runningTasks: string[] = [];

    processedFiles
      .filter((e) => e.running)
      .forEach((e) => {
        if ((e as VirtualMergedFile)._isMergedGroup) {
          // 对于合并组，只显示未完成的文件进度
          const virtualFile = e as VirtualMergedFile;
          virtualFile._mergedInfo.files
            .filter((f) => f.downloaded < f.size) // 只显示未完成的文件
            .forEach((f) => {
              runningTasks.push(
                `${basename(f.file_name)} ${formatSize(f.downloaded)}/${formatSize(f.size)}`,
              );
            });
        } else {
          // 单文件正常显示
          runningTasks.push(
            `${basename(e.file_name)} ${formatSize(e.downloaded)}/${formatSize(e.size)}`,
          );
        }
      });

    current.value = `
      <span class="d-single-stat">${downloaded} / ${total} (${speed}/s)</span>
      <div class="d-single-list">
        <div class="d-single">
          ${runningTasks.join('</div><div class="d-single">')}
        </div>
      </div>
    `;
    percent.value = 20 + (downloadedTotalSize / total_size) * 80;
  }, 30);

  // 使用动态任务管理器进行下载
  const downloadContext: DownloadContext = {
    dfsSource: selectedSource.value,
    extras: INSTALLER_CONFIG.args.dfs_extras,
    local: INSTALLER_CONFIG.embedded_files || [],
    source: source.value,
    hashKey: hashKey as DfsMetadataHashType,
    elevate: needElevate.value,
  };

  const taskManager = new DownloadTaskManager(processedFiles);

  // 初始化任务
  processedFiles.forEach((item) => {
    let task;

    if ((item as VirtualMergedFile)._isMergedGroup) {
      task = new MergedGroupTask(
        item as VirtualMergedFile,
        downloadContext,
        taskManager,
      );
    } else {
      // 根据文件模式选择合适的任务类型
      const file = item as DfsUpdateTask;
      const mode = getFileInstallMode(
        file,
        INSTALLER_CONFIG.embedded_files || [],
        hashKey as DfsMetadataHashType,
      );

      if (mode === 'local') {
        task = new LocalFileTask(file, downloadContext);
      } else {
        // hybridpatch, patch, direct 都使用 SingleFileTask
        task = new SingleFileTask(file, downloadContext, taskManager);
      }
    }

    taskManager.addTask(task);
  });

  await taskManager.waitForCompletion();

  const stats = taskManager.getStats();
  log('All tasks completed successfully:', stats);
  clearInterval(progressInterval.value);

  // Create snapshot of networkInsights before any cleanup to ensure consistent reporting
  const serversSnapshot = [...networkInsights];

  // Clean up DFS2 sessions immediately after download completion, before post-processing
  await cleanupAllDfs2Sessions(serversSnapshot);

  // Clean up plugin sessions
  if (plugin?.endSession) {
    try {
      const cleanUrl = pluginManager.getCleanUrl(selectedSource.value);
      if (cleanUrl) {
        await plugin.endSession(cleanUrl, { servers: serversSnapshot });
      }
    } catch (e) {
      warn('Plugin session cleanup failed:', e);
    }
  }

  if (
    latest_meta.deletes &&
    Array.isArray(latest_meta.deletes) &&
    latest_meta.deletes.length > 0
  ) {
    current.value = t('status.deletingOld');
    try {
      // 过滤掉 ignoreFolderPath 中的文件
      const filesToDelete = latest_meta.deletes.filter((deleteFile) => {
        // 如果是更新场景且有 ignoreMap（已检查过的非空文件夹）
        if (isUpdate.value && ignoreMap.length > 0) {
          // 构造待删除文件的完整路径
          const deleteFullPath = `${source.value}${sep()}${deleteFile}`
            .toLowerCase()
            .replace(/[\\\/]+/g, sep());

          // 检查文件是否在任何需要忽略的文件夹下
          const shouldIgnore = ignoreMap.some((ignoreFolder) => {
            return deleteFullPath.startsWith(ignoreFolder);
          });

          // 如果应该忽略，则不删除（返回 false）
          return !shouldIgnore;
        }
        // 默认情况下，保留在删除列表中
        return true;
      });

      await ipcRmList(
        filesToDelete.map((e) => `${source.value}${sep()}${e}`),
        needElevate.value,
      );
    } catch (e) {
      warn(e);
    }
  }

  await installRuntimes();

  current.value = t('status.almostDone');
  await finishInstall(latest_meta);
  current.value = isUpdate.value ? t('finish.updated') : t('finish.installed');
  step.value = 3;
  percent.value = 100;
}

async function runMirrorcInstall() {
  if (!mirrorcKey.value) {
    changeSelectedSource(selectedSource.value);
    return;
  }
  step.value = 2;
  let source_version = {
    product_version: '',
  } as { product_version: string };
  try {
    source_version = await invoke<{ product_version: string }>(
      'get_exe_version',
      {
        exeName: `${source.value}${sep()}${PROJECT_CONFIG.exeName}`,
      },
    );
  } catch (e) {}
  const source_url = new URL(selectedSource.value);
  if (!source_url.hostname) {
    await dialog_error(
      t('err.mirrorcBadSource', { url: selectedSource.value }),
      t('common.error'),
    );
    error('Invalid Mirrorc source URL:', selectedSource.value);
    step.value = 1;
    return;
  }
  const mirrorc_status = await invoke<MirrorcUpdate>('get_mirrorc_status', {
    resourceId: source_url.hostname,
    cdk: mirrorcKey.value,
    currentVersion: source_version.product_version,
    channel: source_url.searchParams.get('channel') || 'stable',
    arch: source_url.searchParams.get('arch') || undefined,
    os: source_url.searchParams.get('os') || undefined,
  }).catch((e) => {
    return Promise.reject(t('err.mirrorcFetchFailed', { err: String(e) }));
  });
  const errorResult = processMirrorcError(mirrorc_status, 'install');
  if (errorResult) {
    await dialog_error(errorResult.message, t('common.error'));
    if (errorResult.showSourceDialog) {
      dialog.value = 'source';
    }
    step.value = 1;
    return;
  }
  if (mirrorc_status.data?.version_name === source_version.product_version) {
    await finishInstall();
    percent.value = 100;
    step.value = 4;
    return;
  }
  if (
    await installPrepare(
      `${mirrorc_status.data?.version_name || 'unknown'}`,
      true,
    )
  )
    return runMirrorcInstall();
  if (!mirrorc_status.data?.url) {
    await dialog_error(t('err.mirrorcNoUrl'), t('common.error'));
    return;
  }
  if (!mirrorc_status.data?.sha256) {
    await dialog_error(t('err.mirrorcNoSha'), t('common.error'));
    return;
  }
  console.log(mirrorc_status);
  log('Mirrorc source version', source_version.product_version);
  log('Mirrorc target version', mirrorc_status.data.version_name);
  log('Mirrorc update mode', mirrorc_status.data.update_type);
  log('Mirrorc URL', mirrorc_status.data.url);
  const mirrorc_zip_url = mirrorc_status.data.url;
  const mirrorc_zip_path = `${source.value}${sep()}KachinaInstaller_Mirrorc_${mirrorc_status.data.sha256}.zip`;
  subStep.value = 1;
  percent.value = 5;
  current.value = t('status.mirrorcPreparing');
  let lastDownloaded = 0;
  let lastSpeedCalcTime = 0;
  let lastSpeedStr = '';
  await ipcRunMirrorcDownload(
    mirrorc_zip_url,
    mirrorc_zip_path,
    ({ payload }) => {
      if (payload.type === 'download') {
        const { downloaded, total } = payload;
        if (lastSpeedCalcTime !== 0) {
          const now = performance.now();
          const time_diff = now - lastSpeedCalcTime;
          if (time_diff > 100) {
            const speed = (downloaded - lastDownloaded) / time_diff;
            lastDownloaded = downloaded;
            lastSpeedCalcTime = now;
            lastSpeedStr = `(${formatSize(speed * 1000)}/s)`;
            lastSpeedCalcTime = now;
          }
        } else {
          lastSpeedCalcTime = performance.now();
        }
        current.value = `${formatSize(downloaded)} / ${formatSize(
          total,
        )} ${lastSpeedStr}`;
        percent.value = 5 + (downloaded / total) * 65;
      }
    },
    needElevate.value,
  );
  subStep.value = 2;
  current.value = t('status.checkingZip');
  const [meta, changeset] = await ipcRunMirrorcInstall(
    mirrorc_zip_path,
    source.value,
    ({ payload }) => {
      console.log(payload);
      switch (payload.type) {
        case 'extract':
          current.value = `<div class="d-single-stat">${t('status.extracting', { file: payload.file })}</div>`;
          percent.value = 70 + (payload.count / payload.total) * 25;
          break;
        case 'delete':
          current.value = `<div class="d-single-stat">${t('status.deleting', { file: payload.file })}</div>`;
          percent.value = 97;
          break;
      }
    },
    needElevate.value,
  );
  console.log(changeset, meta);
  await installRuntimes();

  current.value = t('status.almostDone');
  await finishInstall(meta);
  current.value = isUpdate.value ? t('finish.updated') : t('finish.installed');
  step.value = 3;
  percent.value = 100;
}

async function getLnkPath() {
  const [program, desktop] = await invoke<InvokeGetDirsRes>('get_dirs', {
    elevated: needElevate.value,
  });
  return {
    programFolder: `${program}${sep()}${PROJECT_CONFIG.appName}`,
    program: `${program}${sep()}${PROJECT_CONFIG.appName}${sep()}${PROJECT_CONFIG.appName}.lnk`,
    desktop: `${desktop}${sep()}${PROJECT_CONFIG.appName}.lnk`,
    uninstall: `${program}${sep()}${PROJECT_CONFIG.appName}${sep()}${t('lnk.uninstallName', { appName: PROJECT_CONFIG.appName })}.lnk`,
  };
}

async function finishInstall(
  latest_meta?: InvokeGetDfsMetadataRes,
): Promise<void> {
  sendInsight(getInsightBase(), 'finish');
  const { program, desktop, uninstall } = await getLnkPath();
  const exePath = `${source.value}${sep()}${PROJECT_CONFIG.exeName}`;
  if (createLnk.value && !isUpdate.value) {
    await ipcCreateLnk(exePath, desktop, needElevate.value).catch(warn);
  }
  if (!isUpdate.value) {
    await ipcCreateLnk(exePath, program, needElevate.value).catch(warn);
  }
  if (
    !isUpdate.value ||
    INSTALLER_CONFIG.install_path_source.startsWith('REG')
  ) {
    try {
      await ipcCreateUninstaller(
        source.value,
        PROJECT_CONFIG.uninstallName,
        PROJECT_CONFIG.updaterName,
        needElevate.value,
      );
    } catch (e) {
      dialog_error(
        t('err.createUninstallerFailed', { err: String(e) }),
        t('common.error'),
      );
      warn(e);
    }
    // 卸载快捷方式文件名随界面语言变化，清理另一种语言可能残留的旧快捷方式
    const otherLang = t('lnk.uninstallName', {
      appName: PROJECT_CONFIG.appName,
    }).startsWith('卸载')
      ? tFor('en', 'lnk.uninstallName', {
          appName: PROJECT_CONFIG.appName,
        })
      : tFor('zh', 'lnk.uninstallName', {
          appName: PROJECT_CONFIG.appName,
        });
    const staleUninstallLnk = `${program}${sep()}${PROJECT_CONFIG.appName}${sep()}${otherLang}.lnk`;
    if (staleUninstallLnk.toLowerCase() !== uninstall.toLowerCase()) {
      await ipcRmList([staleUninstallLnk], needElevate.value).catch(log);
    }
    await ipcCreateLnk(
      `${source.value}${sep()}${PROJECT_CONFIG.uninstallName}`,
      uninstall,
      needElevate.value,
    ).catch(log);
  }
  if (latest_meta) {
    try {
      await ipcWriteRegistry(
        {
          reg_name: PROJECT_CONFIG.regName,
          name: PROJECT_CONFIG.appName,
          version: latest_meta.tag_name || '0.0',
          exe: `${source.value}${sep()}${PROJECT_CONFIG.exeName}`,
          source: source.value,
          uninstaller: `${source.value}${sep()}${PROJECT_CONFIG.uninstallName}`,
          metadata: JSON.stringify(latest_meta),
          size:
            latest_meta.hashed?.reduce((acc, cur) => acc + cur.size, 0) ?? 0,
          publisher: PROJECT_CONFIG.publisher,
        },
        needElevate.value,
      );
    } catch (e) {
      warn(e);
      // 安装或注册表定位到的更新（旧行为）写入失败时提示用户；
      // 从安装目录直接运行的更新（新行为）失败时只记日志，避免每次更新后弹无法处理的错误
      if (
        !isUpdate.value ||
        INSTALLER_CONFIG.install_path_source.startsWith('REG')
      ) {
        dialog_error(
          t('err.writeRegistryFailed', { err: String(e) }),
          t('common.error'),
        );
      }
    }
  }
  if (INSTALLER_CONFIG.args.silent) {
    const win = getCurrentWindow();
    win.close();
  }
}

async function install(): Promise<void> {
  try {
    void cleanupAllDfs2Sessions();
  } catch (e) {}
  try {
    if (installMode.value === 'mirrorc') {
      await runMirrorcInstall();
    } else {
      await runInstall();
    }
  } catch (e) {
    error(e);
    const errstr =
      e instanceof Error
        ? e.message || e.toString() // 使用 message 而不是 stack，更用户友好
        : typeof e === 'string'
          ? e
          : JSON.stringify(e);
    const logErrStr =
      e instanceof Error
        ? e.stack || e.toString() // 日志中保留完整的 stack
        : errstr;
    sendInsight(getInsightBase(), 'error', { error: logErrStr });
    await dialog_error(errstr);

    // Clean up DFS2 sessions on error (only for DFS mode)
    if (installMode.value === 'default') {
      // Create snapshot of networkInsights before any cleanup to ensure consistent reporting
      const serversSnapshot = [...networkInsights];

      await cleanupAllDfs2Sessions(serversSnapshot);

      // 清理插件会话
      const plugin = pluginManager.findPlugin(selectedSource.value);
      if (plugin?.endSession) {
        try {
          const cleanUrl = pluginManager.getCleanUrl(selectedSource.value);
          if (cleanUrl) {
            await plugin.endSession(cleanUrl, { servers: serversSnapshot });
          }
        } catch (e) {
          warn('Plugin session cleanup failed:', e);
        }
      }
    }

    step.value = 1;
    subStep.value = 0;
    percent.value = 0;
    current.value = '';
    clearInterval(progressInterval.value);
    progressInterval.value = 0;
  }
}

function processEmbeddedImage(base64Data: string | null) {
  if (!base64Data) {
    // No embedded image, use default
    imageSource.value = new URL('./left.webp', import.meta.url).href;
    return;
  }

  try {
    // Decode base64 to check first 16 bytes
    const binaryString = atob(base64Data);
    const bytes = new Uint8Array(binaryString.length);
    for (let i = 0; i < binaryString.length; i++) {
      bytes[i] = binaryString.charCodeAt(i);
    }

    // Check if first 16 bytes are all printable ASCII (0x20-0x7E)
    const first16Bytes = bytes.slice(0, Math.min(16, bytes.length));
    const isAscii = first16Bytes.every((byte) => byte >= 0x20 && byte <= 0x7e);

    if (isAscii) {
      // It's CSS - decode and inject
      const cssContent = new TextDecoder().decode(bytes);
      dynamicCss.value = cssContent;
      useDynamicCss.value = true;
      log('Loaded embedded CSS stylesheet');
    } else {
      // It's an image - use as data URI
      imageSource.value = `data:image/webp;base64,${base64Data}`;
      useDynamicCss.value = false;
      log('Loaded embedded image');
    }
  } catch (e) {
    error('Failed to process embedded image:', e);
    // Fallback to default
    imageSource.value = new URL('./left.webp', import.meta.url).href;
  }
}

onMounted(async () => {
  try {
    const win = getCurrentWindow();
    const ps = [];
    ps.push(win.setTitle(' '));
    if (process.env.NODE_ENV === 'development') {
      ps.push(win.show());
    }
    let rsrc = await getSource(false);
    Object.assign(INSTALLER_CONFIG, rsrc);
    if (!rsrc.args.silent) {
      await win.show();
    }
    await Promise.all(ps);
    rsrc = await getSource(true);
    Object.assign(INSTALLER_CONFIG, rsrc);
    log('INSTALLER_CONFIG: ', {
      ...rsrc,
      embedded_config: {
        ...rsrc.embedded_config,
        source: Array.isArray(rsrc.embedded_config?.source)
          ? rsrc.embedded_config?.source.map((e) => ({ id: e.id, uri: e.uri }))
          : rsrc.embedded_config?.source,
      },
      embedded_index: undefined,
      embedded_files: undefined,
      embedded_image: undefined,
      enbedded_metadata: undefined,
    });
    if (INSTALLER_CONFIG.embedded_config) {
      Object.assign(PROJECT_CONFIG, INSTALLER_CONFIG.embedded_config);
      setLanguage(PROJECT_CONFIG.language || 'auto');
      // Process embedded image/CSS
      processEmbeddedImage(INSTALLER_CONFIG.embedded_image);

      if (process.env.NODE_ENV === 'development') {
        if (
          INSTALLER_CONFIG.embedded_files &&
          INSTALLER_CONFIG.embedded_files.length > 0 &&
          !INSTALLER_CONFIG.embedded_files.find((e) => e.name === '\0CONFIG')
        ) {
          dialog_error(t('err.packConfigMissing'));
        }
      }
    } else if (process.env.NODE_ENV === 'development') {
      dialog_error(t('err.configNotFoundDev'));
    } else {
      await dialog_error(t('err.packBroken'));
      const win = getCurrentWindow();
      win.close();
      return;
    }
    const xsrc = rsrc.embedded_config?.source;
    if (!xsrc) {
      throw new Error(t('err.packConfigMissing'));
    }
    if (!Array.isArray(xsrc)) {
      selectedSource.value = xsrc;
    } else if (xsrc.length > 0) {
      selectedSource.value =
        xsrc.find((e) => e.id === rsrc.args.source)?.uri || xsrc[0]?.uri;
    }
    source.value =
      INSTALLER_CONFIG.args.target || INSTALLER_CONFIG.install_path;
    const seldir = await invoke<InvokeSelectDirRes>('select_dir', {
      exeName: PROJECT_CONFIG.exeName,
      silent: true,
      path: source.value,
    });
    if (seldir) {
      setUacByState(seldir.state, PROJECT_CONFIG.uacStrategy);
    }
    if (INSTALLER_CONFIG.embedded_index && INSTALLER_CONFIG.embedded_files) {
      let hasWrongIndex = false;
      for (const i of INSTALLER_CONFIG.embedded_index) {
        const target = INSTALLER_CONFIG.embedded_files.find(
          (e) => e.name === i.name,
        );
        if (!target) {
          log('Unfound index', target, i);
          hasWrongIndex = true;
          continue;
        }
        if (target.offset !== i.offset || target.raw_offset !== i.raw_offset) {
          log('Wrong index: pack=', target, 'index=', i);
          hasWrongIndex = true;
        }
      }
      if (hasWrongIndex) {
        if (process.env.NODE_ENV === 'development') {
          dialog_error(t('err.packIndexWrong'));
        } else {
          await dialog_error(t('err.packBroken'));
          const win = getCurrentWindow();
          win.close();
          return;
        }
      }
    }
    sendInsight(getInsightBase());
    if (INSTALLER_CONFIG.install_path_exists) isUpdate.value = true;
    await win.setTitle(PROJECT_CONFIG.windowTitle);
    INSTALLER_CONFIG.is_uninstall =
      INSTALLER_CONFIG.is_uninstall || INSTALLER_CONFIG.args.uninstall;
    if (INSTALLER_CONFIG.is_uninstall) {
      const uninstallConfig = await invoke(
        'read_uninstall_metadata',
        PROJECT_CONFIG,
      ).catch(log);
      log('UNINSTALL_METADATA: ', uninstallConfig);
      if (!uninstallConfig) {
        await dialog_error(t('err.uninstallMetaMissing'));
        if (process.env.NODE_ENV !== 'development') {
          const win = getCurrentWindow();
          win.close();
        }
        return;
      }
    }
    init.value = 1;
    // Apply window borderless setting
    if (PROJECT_CONFIG.windowBorderless === true) {
      try {
        await getCurrentWindow().setDecorations(false);
      } catch (e) {
        warn('Failed to set window borderless:', e);
      }
    } else {
      try {
        await getCurrentWindow().setDecorations(true);
      } catch (e) {
        warn('Failed to set window decorations:', e);
      }
    }
    init.value = 2;
    if (INSTALLER_CONFIG.args.silent || INSTALLER_CONFIG.args.non_interactive) {
      if (INSTALLER_CONFIG.args.uninstall || INSTALLER_CONFIG.is_uninstall) {
        uninstall();
      } else {
        install();
      }
    }
  } catch (e) {
    error(e);
    if (e instanceof Error)
      await dialog_error(e.stack || e.toString(), t('err.initFailed'));
    else
      await dialog_error(
        typeof e === 'string' ? e : JSON.stringify(e),
        t('err.initFailed'),
      );
    if (process.env.NODE_ENV !== 'development') {
      const win = getCurrentWindow();
      win.close();
    }
  }
});

// Cleanup on component unmount
onUnmounted(() => {
  resetHiddenSourcesState();
});

function formatSize(size: number): string {
  if (size < 1024) {
    return `${size.toFixed(2)} B`;
  }
  if (size < 1024 * 1024) {
    return `${(size / 1024).toFixed(2)} KB`;
  }
  return `${(size / 1024 / 1024).toFixed(2)} MB`;
}

function basename(path: string): string {
  return path.replace(/\\/g, '/').split('/').pop() as string;
}

async function launch() {
  const mainExe = PROJECT_CONFIG.exeName;
  const fullPath = `${source.value}${sep()}${mainExe}`;
  await invoke('launch_and_exit', { path: fullPath });
}
async function exit() {
  const win = getCurrentWindow();
  win.close();
}

async function changeSource() {
  try {
    const seldir = await invoke<InvokeSelectDirRes>('select_dir', {
      path: source.value,
      exeName: PROJECT_CONFIG.exeName,
      silent: false,
    });
    if (seldir === null) return;
    log('SELECT_DIR: ', seldir);
    setUacByState(seldir.state, PROJECT_CONFIG.uacStrategy);
    isUpdate.value = seldir.upgrade;
    if (!seldir.empty && !seldir.upgrade) {
      const isDriveRoot = seldir.path.replace(/\\/g, '/').match(/^\w:\/$/);
      const confirmRes =
        isDriveRoot ||
        (await confirm(t('confirm.dirNotEmpty'), t('common.notice')));
      if (confirmRes) {
        source.value =
          `${seldir.path}${sep()}${PROJECT_CONFIG.appName}`.replace(
            /\\\\/g,
            '\\',
          );
      } else {
        source.value = seldir.path;
      }
    } else {
      source.value = seldir.path;
    }
  } catch (e) {
    if (e instanceof Error) await dialog_error(e.stack || e.toString());
    else await dialog_error(JSON.stringify(e));
    throw e;
  }
}

const mirrorcTempUrl = ref('');
const mirrorcTempKey = ref('');
const mirrorcChecking = ref(false);
async function changeSelectedSource(url: string) {
  const isMirrorc = url.startsWith('mirrorc://');
  dialog.value = isMirrorc ? 'mirrorc' : '';
  if (isMirrorc) {
    try {
      mirrorcKey.value = await invoke('wincred_read', {
        target: `KachinaInstaller_MirrorChyanCDK_${PROJECT_CONFIG.appName}`,
      });
    } catch (e) {
      console.warn(e);
    }
    mirrorcTempUrl.value = url;
    mirrorcTempKey.value = mirrorcKey.value;
  } else {
    selectedSource.value = url;
    mirrorcTempUrl.value = '';
    mirrorcTempKey.value = '';
  }
}

async function changeMirrorcKey() {
  if (!mirrorcTempKey.value) {
    try {
      await invoke('wincred_delete', {
        target: `KachinaInstaller_MirrorChyanCDK_${PROJECT_CONFIG.appName}`,
      });
      mirrorcKey.value = '';
    } catch (e) {
      console.warn(e);
    }
  } else {
    if (mirrorcChecking.value) return;
    mirrorcChecking.value = true;
    const source_url = new URL(mirrorcTempUrl.value);
    if (!source_url.hostname) {
      await dialog_error(
        t('err.mirrorcBadSource', { url: selectedSource.value }),
        t('common.error'),
      );
      mirrorcChecking.value = false;
      return;
    }
    const mirrorc_status = await invoke<MirrorcUpdate>('get_mirrorc_status', {
      resourceId: source_url.hostname,
      cdk: mirrorcTempKey.value,
      currentVersion: '',
      channel: source_url.searchParams.get('channel') || 'stable',
      arch: source_url.searchParams.get('arch') || undefined,
      os: source_url.searchParams.get('os') || undefined,
    });
    const errorResult = processMirrorcError(mirrorc_status, 'cdk-validation');
    if (errorResult) {
      await dialog_error(errorResult.message, t('common.error'));
      mirrorcChecking.value = false;
      return;
    }
    mirrorcKey.value = mirrorcTempKey.value;
    try {
      await invoke('wincred_write', {
        target: `KachinaInstaller_MirrorChyanCDK_${PROJECT_CONFIG.appName}`,
        token: mirrorcTempKey.value,
        comment: 'MirrorChyan CDK for BetterGI',
      });
    } catch (e) {
      console.warn(e);
    }
  }
  selectedSource.value = mirrorcTempUrl.value;
  dialog.value = '';
  mirrorcChecking.value = false;
}

async function dialog_error(
  message: string,
  title = t('common.error'),
): Promise<void> {
  await invoke('error_dialog', {
    message: message.replace(new RegExp(location.origin, 'g'), ''),
    title,
  });
  if (INSTALLER_CONFIG.args.silent) {
    const win = getCurrentWindow();
    win.close();
  }
}
async function confirm(
  message: string,
  title = t('common.notice'),
): Promise<boolean> {
  return await invoke<boolean>('confirm_dialog', { message, title });
}
async function uninstall() {
  step.value = 5;
  sendInsight(getInsightBase(), 'uninstall');
  try {
    const uninstallConfig = (await invoke(
      'read_uninstall_metadata',
      PROJECT_CONFIG,
    )) as InvokeGetDfsMetadataRes;
    if (!uninstallConfig) {
      throw new Error(t('err.uninstallMetaMissing'));
    }
    await ipPrepare(needElevate.value);
    const { programFolder, desktop } = await getLnkPath();
    await ipcRunUninstall(
      {
        source: INSTALLER_CONFIG.install_path,
        files: [
          ...uninstallConfig.hashed.map((e) => e.file_name),
          PROJECT_CONFIG.updaterName,
        ],
        user_data_path: deleteUserData.value
          ? PROJECT_CONFIG.userDataPath.map(replacePathEnvirables)
          : [],
        extra_uninstall_path: [
          ...(PROJECT_CONFIG.extraUninstallPath?.map(replacePathEnvirables) ||
            []),
          programFolder,
          desktop,
        ],
        reg_name: PROJECT_CONFIG.regName,
        uninstall_name: PROJECT_CONFIG.uninstallName,
      },
      needElevate.value,
    );
    step.value = 6;
    if (INSTALLER_CONFIG.args.silent) {
      const win = getCurrentWindow();
      win.close();
    }
  } catch (e) {
    error(e);
    const errstr =
      e instanceof Error
        ? e.stack || e.toString()
        : typeof e === 'string'
          ? e
          : JSON.stringify(e);
    await dialog_error(errstr);
    await sendInsight(getInsightBase(), 'error', { error: errstr });
    step.value = 1;
  }
}

function tplReplace(template: string, data: Record<string, string>): string {
  const regex = /\${(.*?)}/g;
  return template.replace(regex, (_match, key) => {
    return typeof data[key] !== 'undefined' ? data[key] : '';
  });
}
function replacePathEnvirables(path: string): string {
  return tplReplace(path, {
    INSTALL_PATH: INSTALLER_CONFIG.install_path,
    APP_NAME: PROJECT_CONFIG.appName,
  });
}
function setUacByState(
  state: 'Unwritable' | 'Writable' | 'Private',
  uacStrategy: ProjectConfig['uacStrategy'],
) {
  needElevate.value = false;
  switch (uacStrategy) {
    case 'force':
      needElevate.value = true;
      break;
    case 'prefer-admin':
      needElevate.value = state !== 'Private';
      break;
    case 'prefer-user':
      needElevate.value = state === 'Unwritable';
      break;
  }
}
function openMirrorc() {
  invoke('launch', {
    path: `https://mirrorchyan.com/?source=Kachina${PROJECT_CONFIG.appName}`,
  });
}

// Hidden sources easter egg functionality
function handleKeyDown(event: KeyboardEvent) {
  // Only handle comma key when source dialog is open
  if (
    dialog.value !== 'source' ||
    (event.key !== ',' && event.code !== 'Comma')
  ) {
    return;
  }

  event.preventDefault();

  // Clear existing timeout
  if (commaTimeout.value) {
    clearTimeout(commaTimeout.value);
  }

  // Increment comma count
  commaCount.value++;

  // Check if we've reached 5 consecutive comma presses
  if (commaCount.value >= 5) {
    showHiddenSources.value = true;
    commaCount.value = 0; // Reset counter
    return;
  }

  // Set timeout to reset counter after 2 seconds
  commaTimeout.value = setTimeout(() => {
    commaCount.value = 0;
    commaTimeout.value = 0;
  }, 2000);
}

function resetHiddenSourcesState() {
  commaCount.value = 0;
  showHiddenSources.value = false;
  if (commaTimeout.value) {
    clearTimeout(commaTimeout.value);
    commaTimeout.value = 0;
  }
}
const minimize = async () => {
  const win = getCurrentWindow();
  win.minimize();
};
const close = async () => {
  const win = getCurrentWindow();
  win.close();
};
</script>
