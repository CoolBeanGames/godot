/**************************************************************************/
/*  test_crete_csharp_defaults.cpp                                    */
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

TEST_FORCE_LINK(test_crete_csharp_defaults)

#include "core/object/script_language.h"

namespace TestCreteCSharpDefaults {

// Crete Step 9: C# is the only scripting language, so the new-script dialog
// defaults to it (see ScriptCreateDialog's default_language selection).

TEST_SUITE("Crete C# defaults") {
	TEST_CASE("C# is the only registered scripting language") {
		REQUIRE_EQ(ScriptServer::get_language_count(), 1);
		ScriptLanguage *lang = ScriptServer::get_language(0);
		REQUIRE(lang != nullptr);
		CHECK_EQ(lang->get_name(), String("C#"));
		CHECK_EQ(lang->get_type(), String("CSharpScript"));
		CHECK_EQ(lang->get_extension(), String("cs"));
	}

	TEST_CASE("No GDScript language is registered") {
		for (int i = 0; i < ScriptServer::get_language_count(); i++) {
			ScriptLanguage *lang = ScriptServer::get_language(i);
			REQUIRE(lang != nullptr);
			CHECK_NE(lang->get_name(), String("GDScript"));
		}
	}
}

} // namespace TestCreteCSharpDefaults
