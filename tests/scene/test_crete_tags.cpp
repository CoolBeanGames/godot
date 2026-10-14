/**************************************************************************/
/*  test_crete_tags.cpp                                                   */
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

TEST_FORCE_LINK(test_crete_tags)

#include "core/object/class_db.h"
#include "scene/main/node.h"
#include "scene/main/scene_tree.h"
#include "scene/main/window.h"

namespace TestCreteTags {

// NOTE: the test runner (tests/test_main.cpp) only creates a SceneTree for
// cases whose name contains "[SceneTree]". Any case touching the tree MUST
// carry that tag, or SceneTree::get_singleton() is null and the case
// segfaults. Off-tree cases below deliberately omit it (cheaper setup).

TEST_SUITE("Crete tags") {
	TEST_CASE("Tags roundtrip on a node outside the tree") {
		Node *node = memnew(Node);
		CHECK_FALSE(node->is_inside_tree());

		CHECK_FALSE(node->has_tag("enemy"));
		CHECK(node->get_tags().is_empty());

		node->add_tag("enemy");
		CHECK(node->has_tag("enemy"));
		CHECK_EQ(node->get_tags().size(), 1);

		node->add_tag("enemy"); // Duplicate is a no-op.
		CHECK_EQ(node->get_tags().size(), 1);

		node->add_tag("flying");
		CHECK_EQ(String(",").join(node->get_tags()), String("enemy,flying"));

		node->remove_tag("enemy");
		CHECK_FALSE(node->has_tag("enemy"));
		CHECK(node->has_tag("flying"));

		node->remove_tag("enemy"); // Missing tag is a no-op.
		CHECK_EQ(node->get_tags().size(), 1);

		node->remove_tag("flying");
		CHECK(node->get_tags().is_empty());

		memdelete(node);
	}

	TEST_CASE("Empty tag is rejected") {
		Node *node = memnew(Node);
		ERR_PRINT_OFF;
		node->add_tag("");
		ERR_PRINT_ON;
		CHECK_FALSE(node->has_tag(""));
		CHECK_FALSE(node->has_meta("_tags"));
		memdelete(node);
	}

	TEST_CASE("Tags are stored as _tags metadata") {
		Node *node = memnew(Node);
		node->add_tag("enemy");

		REQUIRE(node->has_meta("_tags"));
		CHECK(node->get_meta("_tags").get_type() == Variant::PACKED_STRING_ARRAY);

		node->remove_tag("enemy");
		CHECK_FALSE(node->has_meta("_tags")); // Empty tag lists leave no residue.

		memdelete(node);
	}

	TEST_CASE("Tags metadata is a stored scene property") {
		Node *node = memnew(Node);
		node->add_tag("enemy");

		List<PropertyInfo> props;
		node->get_property_list(&props);
		bool found = false;
		for (const PropertyInfo &pi : props) {
			if (pi.name == StringName("metadata/_tags")) {
				found = true;
				CHECK((pi.usage & PROPERTY_USAGE_STORAGE) != 0);
			}
		}
		CHECK_MESSAGE(found, "metadata/_tags must be listed so scenes persist tags.");

		memdelete(node);
	}

	TEST_CASE("Corrupt _tags metadata fails soft") {
		Node *node = memnew(Node);
		node->set_meta("_tags", 42);

		CHECK(node->get_tags().is_empty());
		CHECK_FALSE(node->has_tag("enemy"));

		node->add_tag("enemy"); // Heals the corrupt value.
		CHECK_EQ(String(",").join(node->get_tags()), String("enemy"));

		memdelete(node);
	}

	TEST_CASE("[SceneTree] get_nodes_with_tag finds nodes in hierarchy order") {
		REQUIRE_MESSAGE(SceneTree::get_singleton() != nullptr, "This case needs the [SceneTree] tag (see note above).");
		Node *root = SceneTree::get_singleton()->get_root();
		Node *a = memnew(Node);
		Node *b = memnew(Node);
		Node *c = memnew(Node);
		Node *d = memnew(Node);
		root->add_child(a);
		root->add_child(b);
		a->add_child(c);
		b->add_child(d);

		a->add_tag("enemy");
		c->add_tag("enemy");
		d->add_tag("enemy");
		d->add_tag("flying");

		Vector<Node *> enemies = SceneTree::get_singleton()->get_nodes_with_tag("enemy");
		REQUIRE_EQ(enemies.size(), 3);
		CHECK_EQ(enemies[0], a);
		CHECK_EQ(enemies[1], c);
		CHECK_EQ(enemies[2], d);

		Vector<Node *> flying = SceneTree::get_singleton()->get_nodes_with_tag("flying");
		REQUIRE_EQ(flying.size(), 1);
		CHECK_EQ(flying[0], d);

		CHECK(SceneTree::get_singleton()->get_nodes_with_tag("nope").is_empty());
		CHECK(SceneTree::get_singleton()->get_nodes_with_tag("").is_empty());

		a->remove_child(c);
		b->remove_child(d);
		root->remove_child(a);
		root->remove_child(b);
		memdelete(a);
		memdelete(b);
		memdelete(c);
		memdelete(d);
	}

	TEST_CASE("Tag methods are exposed to ClassDB") {
		CHECK(ClassDB::has_method("Node", "add_tag"));
		CHECK(ClassDB::has_method("Node", "remove_tag"));
		CHECK(ClassDB::has_method("Node", "has_tag"));
		CHECK(ClassDB::has_method("Node", "get_tags"));
		CHECK(ClassDB::has_method("SceneTree", "get_nodes_with_tag"));
	}
}

} // namespace TestCreteTags
