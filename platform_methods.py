import os
import platform
import shutil
import subprocess
import sys

import methods

# NOTE: The multiprocessing module is not compatible with SCons due to conflict on cPickle


compatibility_platform_aliases = {
    "osx": "macos",
    "iphone": "ios",
    "x11": "linuxbsd",
    "javascript": "web",
}

# CPU architecture options.
architectures = ["x86_32", "x86_64", "arm32", "arm64", "rv64", "ppc64", "wasm32", "wasm64", "loongarch64"]
architecture_aliases = {
    "x86": "x86_32",
    "x64": "x86_64",
    "amd64": "x86_64",
    "armv7": "arm32",
    "armv8": "arm64",
    "arm64v8": "arm64",
    "aarch64": "arm64",
    "rv": "rv64",
    "riscv": "rv64",
    "riscv64": "rv64",
    "ppc64le": "ppc64",
    "loong64": "loongarch64",
}


def detect_arch():
    host_machine = platform.machine().lower()
    if host_machine in architectures:
        return host_machine
    elif host_machine in architecture_aliases.keys():
        return architecture_aliases[host_machine]
    elif "86" in host_machine:
        # Catches x86, i386, i486, i586, i686, etc.
        return "x86_32"
    else:
        methods.print_warning(f'Unsupported CPU architecture: "{host_machine}". Falling back to x86_64.')
        return "x86_64"


def validate_arch(arch, platform_name, supported_arches):
    if arch not in supported_arches:
        methods.print_error(
            'Unsupported CPU architecture "%s" for %s. Supported architectures are: %s.'
            % (arch, platform_name, ", ".join(supported_arches))
        )
        sys.exit(255)


def get_build_version(short):
    import version

    name = "custom_build"
    if os.getenv("BUILD_NAME") is not None:
        name = os.getenv("BUILD_NAME")
    v = "%d.%d" % (version.major, version.minor)
    if version.patch > 0:
        v += ".%d" % version.patch
    status = version.status
    if not short:
        if os.getenv("GODOT_VERSION_STATUS") is not None:
            status = str(os.getenv("GODOT_VERSION_STATUS"))
        v += ".%s.%s" % (status, name)
    return v


def setup_swift_builder(env, apple_platform, sdk_path, current_path, bridging_header_filename, all_swift_files):
    from SCons.Script import Action, Builder

    if apple_platform == "macos":
        target_suffix = "macosx10.9"

    elif apple_platform == "ios":
        target_suffix = "ios14.0"  # iOS 14.0 needed for SwiftUI lifecycle

    elif apple_platform == "iossimulator":
        target_suffix = "ios14.0-simulator"  # iOS 14.0 needed for SwiftUI lifecycle

    elif apple_platform == "visionos":
        target_suffix = "xros26.0"

    elif apple_platform == "visionossimulator":
        target_suffix = "xros26.0-simulator"

    else:
        raise Exception("Invalid platform argument passed to detect_darwin_sdk_path")

    swiftc_target = env["arch"] + "-apple-" + target_suffix

    env["ALL_SWIFT_FILES"] = all_swift_files
    env["CURRENT_PATH"] = current_path
    if "SWIFT_FRONTEND" in env and env["SWIFT_FRONTEND"] != "":
        frontend_path = env["SWIFT_FRONTEND"]
    elif "osxcross" not in env:
        frontend_path = "$APPLE_TOOLCHAIN_PATH/usr/bin/swift-frontend"
    else:
        frontend_path = None

    if frontend_path is None:
        raise Exception("Swift frontend path is not set. Please set SWIFT_FRONTEND.")

    bridging_header_path = current_path + "/" + bridging_header_filename
    env["SWIFTC"] = frontend_path + " -frontend -c"  # Swift compiler
    env["SWIFTCFLAGS"] = [
        "-cxx-interoperability-mode=default",
        "-emit-object",
        "-target",
        swiftc_target,
        "-sdk",
        sdk_path,
        "-import-objc-header",
        bridging_header_path,
        "-swift-version",
        "6",
        "-parse-as-library",
        "-module-name",
        "godot_swift_module",
        "-I./",  # Pass the current directory as the header root so bridging headers can include files from any point of the hierarchy
    ]

    if "osxcross" in env:
        env.Append(
            SWIFTCFLAGS=[
                "-resource-dir",
                "/root/Xcode.app/Contents/Developer/Toolchains/XcodeDefault.xctoolchain/usr/lib/swift",
            ]
        )

    if env["debug_symbols"]:
        env.Append(SWIFTCFLAGS=["-g"])

    if env["optimize"] in ["speed", "speed_trace"]:
        env.Append(SWIFTCFLAGS=["-O"])

    elif env["optimize"] == "size":
        env.Append(SWIFTCFLAGS=["-Osize"])

    elif env["optimize"] in ["debug", "none"]:
        env.Append(SWIFTCFLAGS=["-Onone"])

    def generate_swift_action(source, target, env, for_signature):
        fullpath_swift_files = [env["CURRENT_PATH"] + "/" + file for file in env["ALL_SWIFT_FILES"]]
        fullpath_swift_files.remove(source[0].abspath)

        fullpath_swift_files_string = '"' + '" "'.join(fullpath_swift_files) + '"'
        compile_command = "$SWIFTC " + fullpath_swift_files_string + " -primary-file $SOURCE -o $TARGET $SWIFTCFLAGS"

        swift_comdstr = env.get("SWIFTCOMSTR")
        if swift_comdstr is not None:
            swift_action = Action(compile_command, cmdstr=swift_comdstr)
        else:
            swift_action = Action(compile_command)

        return swift_action

    # Define Builder for Swift files
    swift_builder = Builder(
        generator=generate_swift_action, suffix=env["OBJSUFFIX"], src_suffix=".swift", emitter=methods.redirect_emitter
    )

    env.Append(BUILDERS={"Swift": swift_builder})
    env["BUILDERS"]["Library"].add_src_builder("Swift")
    env["BUILDERS"]["Object"].add_action(".swift", Action(generate_swift_action, generator=1))
