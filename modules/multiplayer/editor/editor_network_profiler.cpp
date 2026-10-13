/**************************************************************************/
/*  editor_network_profiler.cpp                                           */
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

#include "editor_network_profiler.h"

#include "core/object/callable_mp.h"
#include "editor/editor_string_names.h"
#include "editor/run/editor_run_bar.h"
#include "editor/settings/editor_settings.h"
#include "editor/themes/editor_scale.h"
#include "scene/gui/check_box.h"
#include "scene/gui/flow_container.h"
#include "scene/gui/line_edit.h"
#include "scene/main/timer.h"

void EditorNetworkProfiler::_bind_methods() {
	ADD_SIGNAL(MethodInfo("enable_profiling", PropertyInfo(Variant::BOOL, "enable")));
	ADD_SIGNAL(MethodInfo("open_request", PropertyInfo(Variant::STRING, "path")));
}

void EditorNetworkProfiler::_notification(int p_what) {
	switch (p_what) {
		case NOTIFICATION_TRANSLATION_CHANGED: {
			// TRANSLATORS: This is the label for the network profiler's incoming bandwidth.
			down_label->set_text(TTR("Down", "Network"));
			// TRANSLATORS: This is the label for the network profiler's outgoing bandwidth.
			up_label->set_text(TTR("Up", "Network"));

			set_bandwidth(incoming_bandwidth, outgoing_bandwidth);

			if (is_ready()) {
				refresh_rpc_data();
			}
		} break;

		case NOTIFICATION_THEME_CHANGED: {
			if (activate->is_pressed()) {
				activate->set_button_icon(theme_cache.stop_icon);
			} else {
				activate->set_button_icon(theme_cache.play_icon);
			}
			clear_button->set_button_icon(theme_cache.clear_icon);

			incoming_bandwidth_text->set_right_icon(theme_cache.incoming_bandwidth_icon);
			outgoing_bandwidth_text->set_right_icon(theme_cache.outgoing_bandwidth_icon);

			// This needs to be done here to set the faded color when the profiler is first opened
			incoming_bandwidth_text->add_theme_color_override("font_uneditable_color", theme_cache.incoming_bandwidth_color * Color(1, 1, 1, 0.5));
			outgoing_bandwidth_text->add_theme_color_override("font_uneditable_color", theme_cache.outgoing_bandwidth_color * Color(1, 1, 1, 0.5));
		} break;
	}
}

void EditorNetworkProfiler::_update_theme_item_cache() {
	VBoxContainer::_update_theme_item_cache();

	theme_cache.node_icon = get_theme_icon(SNAME("Node"), EditorStringName(EditorIcons));
	theme_cache.stop_icon = get_theme_icon(SNAME("Stop"), EditorStringName(EditorIcons));
	theme_cache.play_icon = get_theme_icon(SNAME("Play"), EditorStringName(EditorIcons));
	theme_cache.clear_icon = get_theme_icon(SNAME("Clear"), EditorStringName(EditorIcons));

	theme_cache.incoming_bandwidth_icon = get_theme_icon(SNAME("ArrowDown"), EditorStringName(EditorIcons));
	theme_cache.outgoing_bandwidth_icon = get_theme_icon(SNAME("ArrowUp"), EditorStringName(EditorIcons));

	theme_cache.incoming_bandwidth_color = get_theme_color(SceneStringName(font_color), EditorStringName(Editor));
	theme_cache.outgoing_bandwidth_color = get_theme_color(SceneStringName(font_color), EditorStringName(Editor));
}

void EditorNetworkProfiler::_refresh() {
	if (!dirty) {
		return;
	}
	dirty = false;
	refresh_rpc_data();
}

void EditorNetworkProfiler::refresh_rpc_data() {
	counters_display->clear();

	TreeItem *root = counters_display->create_item();
	int cols = counters_display->get_columns();

	for (const KeyValue<ObjectID, RPCNodeInfo> &E : rpc_data) {
		TreeItem *node = counters_display->create_item(root);

		for (int j = 0; j < cols; ++j) {
			node->set_text_alignment(j, j > 0 ? HORIZONTAL_ALIGNMENT_RIGHT : HORIZONTAL_ALIGNMENT_LEFT);
		}

		node->set_text(0, E.value.node_path);
		node->set_text(1, E.value.incoming_rpc == 0 ? "-" : vformat(TTR("%d (%s)"), E.value.incoming_rpc, String::humanize_size(E.value.incoming_size)));
		node->set_text(2, E.value.outgoing_rpc == 0 ? "-" : vformat(TTR("%d (%s)"), E.value.outgoing_rpc, String::humanize_size(E.value.outgoing_size)));
	}
}

void EditorNetworkProfiler::_activate_pressed() {
	_update_button_text();

	if (activate->is_pressed()) {
		refresh_timer->start();
	} else {
		refresh_timer->stop();
	}

	emit_signal(SNAME("enable_profiling"), activate->is_pressed());
}

void EditorNetworkProfiler::_update_button_text() {
	if (activate->is_pressed()) {
		activate->set_button_icon(theme_cache.stop_icon);
		activate->set_text(TTRC("Stop"));
	} else {
		activate->set_button_icon(theme_cache.play_icon);
		activate->set_text(TTRC("Start"));
	}
}

void EditorNetworkProfiler::started() {
	_clear_pressed();
	activate->set_disabled(false);

	if (EditorSettings::get_singleton()->get_project_metadata("debug_options", "autostart_network_profiler", false)) {
		set_profiling(true);
		refresh_timer->start();
	}
}

void EditorNetworkProfiler::stopped() {
	activate->set_disabled(true);
	set_profiling(false);
	refresh_timer->stop();
}

void EditorNetworkProfiler::set_profiling(bool p_pressed) {
	activate->set_pressed(p_pressed);
	_update_button_text();
	emit_signal(SNAME("enable_profiling"), activate->is_pressed());
}

void EditorNetworkProfiler::_clear_pressed() {
	rpc_data.clear();
	set_bandwidth(0, 0);
	refresh_rpc_data();
	clear_button->set_disabled(true);
}

void EditorNetworkProfiler::_autostart_toggled(bool p_toggled_on) {
	EditorSettings::get_singleton()->set_project_metadata("debug_options", "autostart_network_profiler", p_toggled_on);
	EditorRunBar::get_singleton()->update_profiler_autostart_indicator();
}

void EditorNetworkProfiler::add_rpc_frame_data(const RPCNodeInfo &p_frame) {
	if (clear_button->is_disabled()) {
		clear_button->set_disabled(false);
	}
	dirty = true;
	if (!rpc_data.has(p_frame.node)) {
		rpc_data.insert(p_frame.node, p_frame);
	} else {
		rpc_data[p_frame.node].incoming_rpc += p_frame.incoming_rpc;
		rpc_data[p_frame.node].outgoing_rpc += p_frame.outgoing_rpc;
	}
	if (p_frame.incoming_rpc) {
		rpc_data[p_frame.node].incoming_size = p_frame.incoming_size / p_frame.incoming_rpc;
	}
	if (p_frame.outgoing_rpc) {
		rpc_data[p_frame.node].outgoing_size = p_frame.outgoing_size / p_frame.outgoing_rpc;
	}
}

void EditorNetworkProfiler::set_bandwidth(int p_incoming, int p_outgoing) {
	incoming_bandwidth = p_incoming;
	outgoing_bandwidth = p_outgoing;

	incoming_bandwidth_text->set_text(vformat(TTR("%s/s"), String::humanize_size(p_incoming)));
	outgoing_bandwidth_text->set_text(vformat(TTR("%s/s"), String::humanize_size(p_outgoing)));

	// Make labels more prominent when the bandwidth is greater than 0 to attract user attention
	incoming_bandwidth_text->add_theme_color_override(
			"font_uneditable_color",
			theme_cache.incoming_bandwidth_color * Color(1, 1, 1, p_incoming > 0 ? 1 : 0.5));
	outgoing_bandwidth_text->add_theme_color_override(
			"font_uneditable_color",
			theme_cache.outgoing_bandwidth_color * Color(1, 1, 1, p_outgoing > 0 ? 1 : 0.5));
}

bool EditorNetworkProfiler::is_profiling() {
	return activate->is_pressed();
}

EditorNetworkProfiler::EditorNetworkProfiler() {
	FlowContainer *container = memnew(FlowContainer);
	container->add_theme_constant_override(SNAME("h_separation"), 8 * EDSCALE);
	container->add_theme_constant_override(SNAME("v_separation"), 2 * EDSCALE);
	add_child(container);

	activate = memnew(Button);
	activate->set_toggle_mode(true);
	activate->set_text(TTRC("Start"));
	activate->set_disabled(true);
	activate->connect(SceneStringName(pressed), callable_mp(this, &EditorNetworkProfiler::_activate_pressed));
	container->add_child(activate);

	clear_button = memnew(Button);
	clear_button->set_text(TTRC("Clear"));
	clear_button->set_disabled(true);
	clear_button->connect(SceneStringName(pressed), callable_mp(this, &EditorNetworkProfiler::_clear_pressed));
	container->add_child(clear_button);

	CheckBox *autostart_checkbox = memnew(CheckBox);
	autostart_checkbox->set_text(TTRC("Autostart"));
	autostart_checkbox->set_pressed(EditorSettings::get_singleton()->get_project_metadata("debug_options", "autostart_network_profiler", false));
	autostart_checkbox->connect(SceneStringName(toggled), callable_mp(this, &EditorNetworkProfiler::_autostart_toggled));
	container->add_child(autostart_checkbox);

	Control *c = memnew(Control);
	c->set_h_size_flags(SIZE_EXPAND_FILL);
	container->add_child(c);

	HBoxContainer *hb = memnew(HBoxContainer);
	hb->add_theme_constant_override(SNAME("separation"), 8 * EDSCALE);
	container->add_child(hb);

	down_label = memnew(Label);
	down_label->set_focus_mode(FOCUS_ACCESSIBILITY);
	down_label->set_auto_translate_mode(AUTO_TRANSLATE_MODE_DISABLED);
	hb->add_child(down_label);

	incoming_bandwidth_text = memnew(LineEdit);
	incoming_bandwidth_text->set_editable(false);
	incoming_bandwidth_text->set_custom_minimum_size(Size2(120, 0) * EDSCALE);
	incoming_bandwidth_text->set_horizontal_alignment(HORIZONTAL_ALIGNMENT_RIGHT);
	incoming_bandwidth_text->set_accessibility_name(TTRC("Incoming Bandwidth"));
	hb->add_child(incoming_bandwidth_text);

	Control *down_up_spacer = memnew(Control);
	down_up_spacer->set_custom_minimum_size(Size2(30, 0) * EDSCALE);
	hb->add_child(down_up_spacer);

	up_label = memnew(Label);
	up_label->set_focus_mode(FOCUS_ACCESSIBILITY);
	up_label->set_auto_translate_mode(AUTO_TRANSLATE_MODE_DISABLED);
	hb->add_child(up_label);

	outgoing_bandwidth_text = memnew(LineEdit);
	outgoing_bandwidth_text->set_editable(false);
	outgoing_bandwidth_text->set_custom_minimum_size(Size2(120, 0) * EDSCALE);
	outgoing_bandwidth_text->set_horizontal_alignment(HORIZONTAL_ALIGNMENT_RIGHT);
	outgoing_bandwidth_text->set_accessibility_name(TTRC("Outgoing Bandwidth"));
	hb->add_child(outgoing_bandwidth_text);

	// RPC
	counters_display = memnew(Tree);
	add_child(counters_display);
	counters_display->set_custom_minimum_size(Size2(280, 0) * EDSCALE);
	counters_display->set_v_size_flags(SIZE_EXPAND_FILL);
	counters_display->set_h_size_flags(SIZE_EXPAND_FILL);
	counters_display->set_hide_folding(true);
	counters_display->set_hide_root(true);
	counters_display->set_columns(3);
	counters_display->set_column_titles_visible(true);
	counters_display->set_column_title(0, TTRC("Node"));
	counters_display->set_column_expand(0, true);
	counters_display->set_column_clip_content(0, true);
	counters_display->set_column_custom_minimum_width(0, 60 * EDSCALE);
	counters_display->set_column_title(1, TTRC("Incoming RPC"));
	counters_display->set_column_expand(1, false);
	counters_display->set_column_clip_content(1, true);
	counters_display->set_column_custom_minimum_width(1, 120 * EDSCALE);
	counters_display->set_column_title(2, TTRC("Outgoing RPC"));
	counters_display->set_column_expand(2, false);
	counters_display->set_column_clip_content(2, true);
	counters_display->set_theme_type_variation("TreeSecondary");
	counters_display->set_column_custom_minimum_width(2, 120 * EDSCALE);

	refresh_timer = memnew(Timer);
	refresh_timer->set_wait_time(0.5);
	refresh_timer->connect("timeout", callable_mp(this, &EditorNetworkProfiler::_refresh));
	add_child(refresh_timer);
}
