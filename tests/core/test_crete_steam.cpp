/**************************************************************************/
/*  test_crete_steam.cpp                                              */
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

TEST_FORCE_LINK(test_crete_steam)

#include "core/config/engine.h"
#include "core/object/class_db.h"
#include "core/variant/dictionary.h"

namespace TestCreteSteam {

// Crete Step 12: a Steam singleton exists in every build. SDK builds compile
// the real GodotSteam module; local builds compile crete_steam_stub, which
// mirrors the same class/method shapes with honest failures.

TEST_SUITE("Crete Steam") {
	TEST_CASE("Steam singleton is registered") {
		CHECK(ClassDB::class_exists("Steam"));
		REQUIRE(Engine::get_singleton()->has_singleton("Steam"));
		Object *steam = Engine::get_singleton()->get_singleton_object("Steam");
		REQUIRE(steam != nullptr);
	}

	TEST_CASE("Steam API behaves for this build flavor") {
		Object *steam = Engine::get_singleton()->get_singleton_object("Steam");
		REQUIRE(steam != nullptr);

		if (ClassDB::has_method("Steam", "isCreteStub")) {
			// Stub build: assert the honest-failure values.
			CHECK_EQ(steam->call("isCreteStub"), Variant(true));
			CHECK_EQ(steam->call("isSteamRunning"), Variant(false));
			CHECK_EQ(steam->call("getAppID"), Variant(0));
			CHECK_EQ(steam->call("restartAppIfNecessary", 480), Variant(false));
			Variant ret = steam->call("steamInitEx", 0, false);
			REQUIRE_EQ(ret.get_type(), Variant::DICTIONARY);
			Dictionary d = ret;
			CHECK(d.has("status"));
			CHECK(d.has("verbal"));
			CHECK_EQ(int(d["status"]), 1); // FAILED_GENERIC
			CHECK(String(d["verbal"]).length() > 0);
		} else {
			// SDK build: assert the surface exists, but never init (no
			// Steam client here, and init may interact).
			CHECK(ClassDB::has_method("Steam", "steamInitEx"));
			CHECK(ClassDB::has_method("Steam", "isSteamRunning"));
			CHECK(ClassDB::has_method("Steam", "getAppID"));
		}
	}
}

} // namespace TestCreteSteam
