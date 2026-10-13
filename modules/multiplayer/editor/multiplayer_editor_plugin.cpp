/**************************************************************************/
/*  multiplayer_editor_plugin.cpp                                         */
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

#include "multiplayer_editor_plugin.h"

#include "editor_network_profiler.h"

#include "core/object/callable_mp.h"
#include "editor/editor_interface.h"
#include "editor/editor_node.h"
#include "scene/main/scene_tree.h"

void MultiplayerEditorDebugger::_bind_methods() {
	ADD_SIGNAL(MethodInfo("open_request", PropertyInfo(Variant::STRING, "path")));
}

bool MultiplayerEditorDebugger::has_capture(const String &p_capture) const {
	return p_capture == "multiplayer";
}

void MultiplayerEditorDebugger::_open_request(const String &p_path) {
	emit_signal("open_request", p_path);
}

bool MultiplayerEditorDebugger::capture(const String &p_message, const Array &p_data, int p_session) {
	ERR_FAIL_COND_V(!profilers.has(p_session), false);
	EditorNetworkProfiler *profiler = profilers[p_session];
	if (p_message == "multiplayer:rpc") {
		MultiplayerDebugger::RPCFrame frame;
		frame.deserialize(p_data);
		for (int i = 0; i < frame.infos.size(); i++) {
			profiler->add_rpc_frame_data(frame.infos[i]);
		}
		return true;
	} else if (p_message == "multiplayer:bandwidth") {
		ERR_FAIL_COND_V(p_data.size() < 2, false);
		profiler->set_bandwidth(p_data[0], p_data[1]);
		return true;
	}
	return false;
}

void MultiplayerEditorDebugger::_profiler_activate(bool p_enable, int p_session_id) {
	Ref<EditorDebuggerSession> session = get_session(p_session_id);
	ERR_FAIL_COND(session.is_null());
	session->toggle_profiler("multiplayer:bandwidth", p_enable);
	session->toggle_profiler("multiplayer:rpc", p_enable);
}

void MultiplayerEditorDebugger::setup_session(int p_session_id) {
	Ref<EditorDebuggerSession> session = get_session(p_session_id);
	ERR_FAIL_COND(session.is_null());
	EditorNetworkProfiler *profiler = memnew(EditorNetworkProfiler);
	profiler->connect("enable_profiling", callable_mp(this, &MultiplayerEditorDebugger::_profiler_activate).bind(p_session_id));
	profiler->connect("open_request", callable_mp(this, &MultiplayerEditorDebugger::_open_request));
	profiler->set_name(TTRC("Network Profiler"));
	session->connect("started", callable_mp(profiler, &EditorNetworkProfiler::started));
	session->connect("stopped", callable_mp(profiler, &EditorNetworkProfiler::stopped));
	session->add_session_tab(profiler);
	profilers[p_session_id] = profiler;
}

/// MultiplayerEditorPlugin

MultiplayerEditorPlugin::MultiplayerEditorPlugin() {
	debugger.instantiate();
	debugger->connect("open_request", callable_mp(this, &MultiplayerEditorPlugin::_open_request));
}

void MultiplayerEditorPlugin::_open_request(const String &p_path) {
	EditorInterface::get_singleton()->open_scene_from_path(p_path);
}

void MultiplayerEditorPlugin::_notification(int p_what) {
	switch (p_what) {
		case NOTIFICATION_ENTER_TREE: {
			add_debugger_plugin(debugger);
		} break;
		case NOTIFICATION_EXIT_TREE: {
			remove_debugger_plugin(debugger);
		}
	}
}

bool MultiplayerEditorPlugin::handles(Object *p_object) const {
	return false;
}
