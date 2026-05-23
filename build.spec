block_cipher = None
a = Analysis(
    ['app.py'],
    pathex=[],
    binaries=[],
    datas=[
        ('modlist_3.json', '.'),
        ('en-modlist_3.json', '.'),
        ('mod_conflicts.json', '.'),
        ('Modpacks', 'Modpacks'),
        ('static', 'static'),
        ('templates', 'templates'),
        ('TLDPatcher', 'TLDPatcher'),
        ('LICENSE_AND_NOTICE.md', '.'),
        ('translations', 'translations'),
    ],
    hiddenimports=[
        'flask', 'jinja2', 'markupsafe', 'werkzeug',
        'itsdangerous', 'click', 'requests', 'urllib3',
        'chardet', 'certifi', 'idna',
        'queue', 'threading', 'webbrowser',
        'concurrent.futures'
    ],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    excludes=['matplotlib', 'numpy', 'pandas'],
    win_no_prefer_redirects=False,
    win_private_assemblies=False,
    cipher=block_cipher,
    noarchive=False,
)
pyz = PYZ(a.pure, a.zipped_data, cipher=block_cipher)
exe = EXE(
    pyz, a.scripts, a.binaries, a.datas, [],
    name='ModpackManager',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    upx_exclude=[],
    runtime_tmpdir=None,
    console=True,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
    icon='static/tld.ico',
)
coll = COLLECT(
    exe, a.binaries, a.datas,
    strip=False, upx=True, upx_exclude=[],
    name='ModpackManager',
)