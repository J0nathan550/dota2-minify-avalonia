# ui.app

## `Api()`

*No documentation available.*

<details open><summary>Source</summary>

```python
class Api:
    def __init__(self) -> None:
        self._host_shutdown: Any = None
        self.patch_service = PatchService()
        self.mod_service = ModService()
        self.config_service = ConfigService()
        self.plugin_service = PluginService()
        self.dialog_service = DialogService()

    def start_patch(self) -> Dict[str, Any]:
        return self.patch_service.start_patch()

    def start_uninstall(self, remove_everything: bool = False) -> Dict[str, Any]:
        return self.patch_service.start_uninstall(remove_everything)

    def open_url(self, url: str) -> None:
        import webbrowser

        webbrowser.open(url)

    def open_config_directory(self) -> None:
        fs.open_thing(base.config_dir)

    def is_patching(self) -> bool:
        return self.patch_service.is_patching()

    def get_logs(self) -> List[Dict[str, Any]]:
        return self.patch_service.get_logs()

    def get_downloads(self) -> List[Dict[str, Any]]:
        return self.patch_service.get_downloads()

    def clear_logs(self) -> bool:
        return self.patch_service.clear_logs()

    def get_mods(self) -> List[Dict[str, Any]]:
        return self.mod_service.get_mods()

    def get_mod_details(self, mod_name: str, lang: str | None = None) -> Dict[str, Any]:
        return self.mod_service.get_mod_details(mod_name, lang)

    def set_mods(self, data: Dict[str, bool]) -> bool:
        return self.mod_service.set_mods(data)

    def get_available_languages(self) -> List[str]:
        return self.config_service.get_available_languages()

    def is_debug_env(self) -> bool:
        return self.config_service.is_debug_env()

    def get_version(self) -> str:
        return base.VERSION

    def get_localization(self, lang: str = "en") -> Dict[str, str]:
        return self.config_service.get_localization(lang)

    def get_current_locale(self) -> str:
        return self.config_service.get_current_locale()

    def set_locale(self, lang: str) -> bool:
        return self.config_service.set_locale(lang)

    def get_available_game_languages(self) -> List[str]:
        return self.config_service.get_available_game_languages()

    def get_current_game_language(self) -> str:
        return self.config_service.get_current_game_language()

    def set_game_language(self, lang: str) -> bool:
        return self.config_service.set_game_language(lang)

    def get_steam_status(self) -> Dict[str, Any]:
        from core import steam

        library = config.get("steam_library", "")
        dota_found = bool(library) and (
            os.path.exists(os.path.join(library, base.DOTA_EXECUTABLE_PATH))
            or os.path.exists(os.path.join(library, base.DOTA_EXECUTABLE_PATH_FALLBACK))
        )
        return {"root": steam.ROOT, "library": library, "dota_found": dota_found}

    def set_steam_root(self, path: str) -> bool:
        "Paths are resolved at import time, the frontend restarts the backend after this succeeds."
        from core import steam

        steam_root = os.path.normpath(path)
        config.set("steam_root", steam_root)
        return steam.find_library_from_vdf(steam_root)

    def get_steam_accounts(self) -> List[Dict[str, Any]]:
        return self.config_service.get_steam_accounts()

    def get_settings(self) -> Dict[str, Any]:
        return self.config_service.get_settings()

    def set_setting(self, key: str, value: Any, mod_name: str | None = None) -> bool:
        return self.config_service.set_setting(key, value, mod_name)

    def run_mod_function(self, mod_name: str, function_name: str) -> bool:
        return self.config_service.run_mod_function(mod_name, function_name)

    def reset_native_settings(self) -> bool:
        return self.config_service.reset_native_settings()

    def reset_mod_settings(self, mod_name: str) -> bool:
        return self.config_service.reset_mod_settings(mod_name)

    def get_available_themes(self) -> List[Dict[str, str]]:
        return self.config_service.get_available_themes()

    def get_theme_css(self, theme_name: str | None = None) -> str:
        return self.config_service.get_theme_css(theme_name)

    def get_state(self, key: str, default: Any = None) -> Any:
        states = utils.read_states()
        return states.get(key, default)

    def set_state(self, key: str, value: Any) -> bool:
        utils.write_states(key, value)
        return True

    def check_workshop_tools_needed(self) -> bool:
        import conditions

        return base.is_linux and not constants.rescomp_override and conditions.workshop_installed

    def extract_workshop_tools(self) -> bool:
        import helper

        return helper.extract_workshop_tools()

    def get_legacy_mods(self) -> List[str]:
        backup_dirs = sorted(path for path in Path(base.base_dir).glob("mods_old_*") if path.is_dir())
        entries = []
        if backup_dirs:
            backup_dir = backup_dirs[-1]
            mods_dir = Path(base.mods_dir)
            entries.extend(
                f"{backup_dir.name}/{entry.name}"
                for entry in sorted(backup_dir.iterdir())
                if not (mods_dir / entry.name).exists()
            )
        return entries

    def import_legacy_mods(self, import_mods: bool = True) -> Dict[str, Any]:
        migrated = []
        discarded = []
        fs.create_dirs(base.mods_dir)
        backup_dirs = sorted(path for path in Path(base.base_dir).glob("mods_old_*") if path.is_dir())
        backup_dir = backup_dirs[-1] if backup_dirs else None
        if backup_dir:
            for source in sorted(backup_dir.iterdir()):
                destination = Path(base.mods_dir) / source.name
                try:
                    if not import_mods or destination.exists():
                        discarded.append(source.name)
                        fs.remove_path(str(source))
                    else:
                        shutil.move(str(source), str(destination))
                        migrated.append(source.name)
                except OSError:
                    discarded.append(source.name)
                    fs.remove_path(str(source))
            try:
                backup_dir.rmdir()
            except OSError:
                pass
            for stale_dir in backup_dirs[:-1]:
                fs.remove_path(str(stale_dir))
        return {"migrated": migrated, "discarded": discarded}

    def is_workshop_installed(self) -> bool:
        import conditions

        return bool(conditions.workshop_installed)

    def download_workshop_tools(self) -> bool:
        import tempfile
        import requests
        import conditions
        from core import fs, log

        repo_url = (
            "https://github.com/Dota-Modding-Community/workshoptools/releases/latest/download/resourcecompiler.zip"
        )
        api_url = "https://api.github.com/repos/Dota-Modding-Community/workshoptools/releases/latest"
        download_url = repo_url

        try:
            resp = requests.get(api_url, headers={"User-Agent": "dota2-minify"}, timeout=10)
            if resp.status_code == 200:
                assets = resp.json().get("assets", [])
                for asset in assets:
                    if asset.get("name", "").endswith(".zip"):
                        download_url = asset.get("browser_download_url", repo_url)
                        break
        except Exception as e:
            log.write_warning(f"Could not query GitHub API for workshoptools release: {e}")

        temp_dir = os.environ.get("TEMP", os.environ.get("TMP", tempfile.gettempdir()))
        zip_path = os.path.join(temp_dir, "workshoptools.zip")

        output.add_text(f"Downloading Workshop Tools from {download_url}...")
        success = fs.download_file(
            url=download_url,
            target_path=zip_path,
            name="workshoptools.zip",
            task_id="workshoptools",
            emit_progress=True,
        )
        if not success or not os.path.exists(zip_path):
            output.add_text("Failed to download Workshop Tools.", msg_type="error")
            return False

        output.add_text("Expanding Workshop Tools to config/rescomproot...")
        target_dir = base.rescomp_override_dir
        fs.create_dirs(target_dir)
        extract_success = fs.extract_archive(zip_path, target_dir)
        if not extract_success:
            output.add_text("Failed to extract Workshop Tools archive.", msg_type="error")
            return False

        inner_dir = os.path.join(target_dir, "resourcecompiler")
        if os.path.isdir(inner_dir):
            for item in os.listdir(inner_dir):
                src = os.path.join(inner_dir, item)
                dst = os.path.join(target_dir, item)
                if os.path.exists(dst):
                    fs.remove_path(dst)
                fs.move_path(src, dst)
            fs.remove_path(inner_dir)

        fs.remove_path(zip_path)

        constants.recalc_rescomp_dirs()

        if (base.is_linux or base.is_mac) and os.path.exists(constants.dota_resource_compiler_path):
            import stat

            st = os.stat(constants.dota_resource_compiler_path)
            os.chmod(
                constants.dota_resource_compiler_path,
                st.st_mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH,
            )

        compiler_exists = os.path.exists(constants.dota_resource_compiler_path)
        conditions.workshop_installed = compiler_exists

        if compiler_exists:
            output.add_text("Workshop Tools installed successfully!", msg_type="success")
            return True
        else:
            output.add_text(
                f"resourcecompiler.exe was not found at {constants.dota_resource_compiler_path}",
                msg_type="error",
            )
            return False

    def get_plugins(self) -> List[Dict[str, Any]]:
        return self.plugin_service.get_plugins()

    def get_plugin_localization(self, plugin_id: str, lang: str = "en") -> Dict[str, str]:
        return self.plugin_service.get_localization(plugin_id, lang)

    def call_plugin_api(self, plugin_id: str, action: str, params: Dict[str, Any] = None) -> Any:
        return self.plugin_service.call_api(plugin_id, action, params)
```

</details>
