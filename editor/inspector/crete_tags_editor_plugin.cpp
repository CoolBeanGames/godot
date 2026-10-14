/**************************************************************************/
/*  crete_tags_editor_plugin.cpp                                        */
/**************************************************************************/
/*                         This file is part of:                          */
/*                             CRETE ENGINE                               */
/*                   https://github.com/CoolBeanGames                     */
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

#include "crete_tags_editor_plugin.h"

#include "core/object/callable_mp.h"
#include "editor/editor_undo_redo_manager.h"
#include "scene/gui/box_container.h"
#include "scene/gui/label.h"
#include "scene/gui/line_edit.h"
#include "scene/main/node.h"

bool EditorInspectorPluginCreteTags::can_handle(Object *p_object) {
	return Object::cast_to<Node>(p_object) != nullptr;
}

void EditorInspectorPluginCreteTags::parse_begin(Object *p_object) {
	Node *node = Object::cast_to<Node>(p_object);
	if (node == nullptr) {
		return;
	}
	edited_node_id = node->get_instance_id();

	HBoxContainer *row = memnew(HBoxContainer);

	Label *label = memnew(Label);
	label->set_text(TTR("Tags"));
	row->add_child(label);

	LineEdit *edit = memnew(LineEdit);
	edit->set_h_size_flags(Control::SIZE_EXPAND_FILL);
	edit->set_text(String(", ").join(node->get_tags()));
	edit->set_placeholder(TTR("comma-separated · just type"));
	edit->set_tooltip_text(TTR("comma-separated · just type"));
	edit->set_clear_button_enabled(true);
	row->add_child(edit);
	tags_edit_id = edit->get_instance_id();

	edit->connect(SNAME("text_submitted"), callable_mp(this, &EditorInspectorPluginCreteTags::_apply_tags));
	edit->connect(SNAME("focus_exited"), callable_mp(this, &EditorInspectorPluginCreteTags::_apply_focus_lost));

	EditorUndoRedoManager *undo_redo = EditorUndoRedoManager::get_singleton();
	if (!history_connected) {
		undo_redo->connect(SNAME("history_changed"), callable_mp(this, &EditorInspectorPluginCreteTags::_refresh_edit));
		history_connected = true;
	}

	add_custom_control(row);
}

void EditorInspectorPluginCreteTags::_apply_tags(const String &p_text) {
	Node *node = Object::cast_to<Node>(ObjectDB::get_instance(edited_node_id));
	if (node == nullptr) {
		return;
	}

	PackedStringArray tags;
	const PackedStringArray parts = p_text.split(",", false);
	for (int i = 0; i < parts.size(); i++) {
		const String tag = parts[i].strip_edges();
		if (!tag.is_empty() && !tags.has(tag)) {
			tags.append(tag);
		}
	}

	const PackedStringArray old_tags = node->get_tags();
	if (String(",").join(tags) == String(",").join(old_tags)) {
		return;
	}

	EditorUndoRedoManager *undo_redo = EditorUndoRedoManager::get_singleton();
	undo_redo->create_action(TTR("Set Node Tags"));
	if (tags.is_empty()) {
		undo_redo->add_do_method(node, StringName("remove_meta"), StringName("_tags"));
	} else {
		undo_redo->add_do_method(node, StringName("set_meta"), StringName("_tags"), tags);
	}
	if (old_tags.is_empty()) {
		undo_redo->add_undo_method(node, StringName("remove_meta"), StringName("_tags"));
	} else {
		undo_redo->add_undo_method(node, StringName("set_meta"), StringName("_tags"), old_tags);
	}
	undo_redo->commit_action();
	_refresh_edit();
}

void EditorInspectorPluginCreteTags::_apply_focus_lost() {
	LineEdit *edit = Object::cast_to<LineEdit>(ObjectDB::get_instance(tags_edit_id));
	if (edit == nullptr) {
		return;
	}
	_apply_tags(edit->get_text());
}

void EditorInspectorPluginCreteTags::_refresh_edit() {
	Node *node = Object::cast_to<Node>(ObjectDB::get_instance(edited_node_id));
	LineEdit *edit = Object::cast_to<LineEdit>(ObjectDB::get_instance(tags_edit_id));
	if (node == nullptr || edit == nullptr) {
		return;
	}
	edit->set_text(String(", ").join(node->get_tags()));
}
