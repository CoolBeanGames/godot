/**************************************************************************/
/*  test_crete_removals.cpp                                             */
/**************************************************************************/
/*                         This file is part of:                          */
/*                             GODOT ENGINE                               */
/*                        https://godotengine.org                         */
/**************************************************************************/
/* Copyright (c) 2014-present Godot Engine contributors (see AUTHORS.md). */
/* Copyright (c) 2007-2014 Juan Linietsky, Ariel Manzur.                  */
/*                                                                        */
/* Permission is hereby granted, free of charge, to any person obtaining  */
/* a copy of this software and associated documentation files (the        */
/* "Software"), to deal in the Software without restriction, including    */
/* without limitation the rights to use, copy, modify, merge, publish,    */
/* distribute, sublicense, and/or sell copies of the Software, and to     */
/* permit persons to whom the Software is furnished to do so, subject to  */
/* the following conditions:                                              */
/*                                                                        */
/* The above copyright notice and this permission notice shall be         */
/* included in all copies or substantial portions of the Software.        */
/*                                                                        */
/* THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,        */
/* EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF     */
/* MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. */
/* IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY   */
/* CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,   */
/* TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE      */
/* SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.                 */
/**************************************************************************/

#include "tests/test_macros.h"

TEST_FORCE_LINK(test_crete_removals)

#include "core/config/project_settings.h"
#include "core/object/class_db.h"
#include "core/object/method_bind.h"
#include "core/object/script_language.h"

namespace TestCreteRemovals {

TEST_SUITE("Crete removals") {
	TEST_CASE("GDScript class is absent") {
		CHECK_FALSE(ClassDB::class_exists("GDScript"));
	}

	TEST_CASE("No GDScript script language is registered") {
		for (int i = 0; i < ScriptServer::get_language_count(); i++) {
			ScriptLanguage *lang = ScriptServer::get_language(i);
			REQUIRE(lang != nullptr);
			CHECK(lang->get_name() != "GDScript");
		}
	}

	TEST_CASE("C# script language is registered") {
		bool found = false;
		for (int i = 0; i < ScriptServer::get_language_count(); i++) {
			ScriptLanguage *lang = ScriptServer::get_language(i);
			if (lang != nullptr && lang->get_name() == "C#") {
				found = true;
			}
		}
		CHECK_MESSAGE(found, "C# must be the scripting language.");
	}

	TEST_CASE("OpenXR classes are absent") {
		CHECK_FALSE(ClassDB::class_exists("OpenXRInterface"));
		CHECK_FALSE(ClassDB::class_exists("OpenXRAction"));
		CHECK_FALSE(ClassDB::class_exists("OpenXRActionMap"));
		CHECK_FALSE(ProjectSettings::get_singleton()->has_setting("xr/openxr/enabled"));
	}

	TEST_CASE("Node replication classes are absent") {
		CHECK_FALSE(ClassDB::class_exists("MultiplayerSpawner"));
		CHECK_FALSE(ClassDB::class_exists("MultiplayerSynchronizer"));
		CHECK_FALSE(ClassDB::class_exists("SceneReplicationConfig"));
	}

	TEST_CASE("RPC multiplayer classes are present") {
		CHECK(ClassDB::class_exists("MultiplayerAPI"));
		CHECK(ClassDB::class_exists("SceneMultiplayer"));
		CHECK(ClassDB::class_exists("OfflineMultiplayerPeer"));
		CHECK(ClassDB::class_exists("ENetMultiplayerPeer"));
		List<MethodInfo> methods;
		ClassDB::get_method_list("SceneMultiplayer", &methods);
		bool has_replication_api = false;
		for (const MethodInfo &m : methods) {
			if (m.name == "set_max_sync_packet_size" || m.name == "set_max_delta_packet_size") {
				has_replication_api = true;
			}
		}
		CHECK_FALSE(has_replication_api);
	}
}

} // namespace TestCreteRemovals
