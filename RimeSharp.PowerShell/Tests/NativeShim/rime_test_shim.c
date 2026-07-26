#include <rime_api.h>

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <wchar.h>
#include <windows.h>

#define SHIM_EXPORT __declspec(dllexport)
#define SHIM_SESSION_CAPACITY 256
#define SHIM_DELAY_MS 1500
#define SHIM_SCENARIO_CAPACITY 64
#define SHIM_LOG_PATH_CAPACITY 32768

static INIT_ONCE g_once = INIT_ONCE_STATIC_INIT;
static CRITICAL_SECTION g_state_lock;
static RimeApi g_api;
static RimeNotificationHandler g_notification_handler;
static void* g_notification_context;
static unsigned char g_sessions[SHIM_SESSION_CAPACITY];
static LONG g_next_session_id;
static LONG g_initialize_calls;
static LONG g_create_session_calls;
static LONG g_destroy_session_calls;
static LONG g_active_calls;
static LONG g_max_concurrency;
static LONG g_find_session_entries;
static char g_scenario[SHIM_SCENARIO_CAPACITY];
static wchar_t g_log_path[SHIM_LOG_PATH_CAPACITY];
static HANDLE g_find_session_release;
static HANDLE g_second_find_session_entered;

static void shim_setup(RimeTraits* traits);
static void shim_set_notification_handler(RimeNotificationHandler handler,
                                          void* context_object);
static void shim_initialize(RimeTraits* traits);
static void shim_finalize(void);
static Bool shim_start_maintenance(Bool full_check);
static Bool shim_is_maintenance_mode(void);
static void shim_join_maintenance_thread(void);
static void shim_deployer_initialize(RimeTraits* traits);
static Bool shim_deploy(void);
static Bool shim_deploy_config_file(const char* file_name,
                                    const char* version_key);
static RimeSessionId shim_create_session(void);
static Bool shim_find_session(RimeSessionId session_id);
static Bool shim_destroy_session(RimeSessionId session_id);
static Bool shim_process_key(RimeSessionId session_id, int keycode, int mask);
static Bool shim_get_commit(RimeSessionId session_id, RimeCommit* commit);
static Bool shim_free_commit(RimeCommit* commit);
static Bool shim_get_context(RimeSessionId session_id, RimeContext* context);
static Bool shim_free_context(RimeContext* context);
static Bool shim_get_status(RimeSessionId session_id, RimeStatus* status);
static Bool shim_free_status(RimeStatus* status);
static void shim_set_option(RimeSessionId session_id,
                            const char* option,
                            Bool value);
static Bool shim_get_option(RimeSessionId session_id, const char* option);
static Bool shim_simulate_key_sequence(RimeSessionId session_id,
                                       const char* key_sequence);
static const char* shim_get_input(RimeSessionId session_id);

static BOOL CALLBACK shim_initialize_once(PINIT_ONCE once,
                                          PVOID parameter,
                                          PVOID* context) {
  (void)once;
  (void)parameter;
  (void)context;

  InitializeCriticalSection(&g_state_lock);
  g_find_session_release = CreateEventW(NULL, TRUE, FALSE, NULL);
  g_second_find_session_entered = CreateEventW(NULL, TRUE, FALSE, NULL);
  memset(&g_api, 0, sizeof(g_api));
  g_api.data_size = (int)(sizeof(g_api) - sizeof(g_api.data_size));
  g_api.setup = shim_setup;
  g_api.set_notification_handler = shim_set_notification_handler;
  g_api.initialize = shim_initialize;
  g_api.finalize = shim_finalize;
  g_api.start_maintenance = shim_start_maintenance;
  g_api.is_maintenance_mode = shim_is_maintenance_mode;
  g_api.join_maintenance_thread = shim_join_maintenance_thread;
  g_api.deployer_initialize = shim_deployer_initialize;
  g_api.deploy = shim_deploy;
  g_api.deploy_config_file = shim_deploy_config_file;
  g_api.create_session = shim_create_session;
  g_api.find_session = shim_find_session;
  g_api.destroy_session = shim_destroy_session;
  g_api.process_key = shim_process_key;
  g_api.get_commit = shim_get_commit;
  g_api.free_commit = shim_free_commit;
  g_api.get_context = shim_get_context;
  g_api.free_context = shim_free_context;
  g_api.get_status = shim_get_status;
  g_api.free_status = shim_free_status;
  g_api.set_option = shim_set_option;
  g_api.get_option = shim_get_option;
  g_api.simulate_key_sequence = shim_simulate_key_sequence;
  g_api.get_input = shim_get_input;
  return TRUE;
}

static void shim_ensure_initialized(void) {
  InitOnceExecuteOnce(&g_once, shim_initialize_once, NULL, NULL);
}

static const char* shim_scenario(void) {
  return g_scenario;
}

static Bool shim_is_scenario(const char* expected) {
  return strcmp(shim_scenario(), expected) == 0 ? True : False;
}

static void shim_log(const char* event_name) {
  FILE* stream;
  if (!g_log_path[0]) {
    return;
  }

  EnterCriticalSection(&g_state_lock);
  stream = _wfopen(g_log_path, L"a");
  if (stream) {
    fprintf(stream, "%s\n", event_name);
    fclose(stream);
  }
  LeaveCriticalSection(&g_state_lock);
}

static void shim_update_max_concurrency(LONG active) {
  LONG observed = InterlockedCompareExchange(&g_max_concurrency, 0, 0);
  while (active > observed) {
    LONG replaced = InterlockedCompareExchange(
        &g_max_concurrency, active, observed);
    if (replaced == observed) {
      return;
    }
    observed = replaced;
  }
}

static void shim_enter_call(const char* event_name) {
  LONG active = InterlockedIncrement(&g_active_calls);
  shim_update_max_concurrency(active);
  shim_log(event_name);
}

static void shim_leave_call(const char* event_name) {
  shim_log(event_name);
  InterlockedDecrement(&g_active_calls);
}

static Bool shim_session_is_live(RimeSessionId session_id) {
  Bool result = False;
  if (session_id < SHIM_SESSION_CAPACITY) {
    EnterCriticalSection(&g_state_lock);
    result = g_sessions[session_id] ? True : False;
    LeaveCriticalSection(&g_state_lock);
  }
  return result;
}

static void shim_setup(RimeTraits* traits) {
  (void)traits;
  shim_log("setup");
}

static void shim_set_notification_handler(RimeNotificationHandler handler,
                                          void* context_object) {
  EnterCriticalSection(&g_state_lock);
  g_notification_handler = handler;
  g_notification_context = context_object;
  LeaveCriticalSection(&g_state_lock);
  shim_log("set_notification_handler");
}

static void shim_initialize(RimeTraits* traits) {
  LONG call_number;
  (void)traits;
  call_number = InterlockedIncrement(&g_initialize_calls);
  shim_enter_call("initialize_enter");
  if (shim_is_scenario("start_cancel") && call_number == 1) {
    Sleep(SHIM_DELAY_MS);
  }
  shim_leave_call("initialize_exit");
}

static void shim_finalize(void) {
  RimeNotificationHandler handler;
  void* context;
  shim_log("finalize_enter");

  EnterCriticalSection(&g_state_lock);
  handler = g_notification_handler;
  context = g_notification_context;
  LeaveCriticalSection(&g_state_lock);
  if (handler) {
    shim_log("finalize_callback");
    handler(context, 0, "shim", "finalize");
  }

  EnterCriticalSection(&g_state_lock);
  memset(g_sessions, 0, sizeof(g_sessions));
  g_notification_handler = NULL;
  g_notification_context = NULL;
  LeaveCriticalSection(&g_state_lock);
  shim_log("finalize_exit");
}

static Bool shim_start_maintenance(Bool full_check) {
  (void)full_check;
  shim_log("start_maintenance");
  return False;
}

static Bool shim_is_maintenance_mode(void) {
  return False;
}

static void shim_join_maintenance_thread(void) {
  shim_log("join_maintenance_thread");
}

static void shim_deployer_initialize(RimeTraits* traits) {
  (void)traits;
  shim_log("deployer_initialize");
}

static Bool shim_deploy(void) {
  shim_log("deploy");
  return shim_is_scenario("deploy_fail") ? False : True;
}

static Bool shim_deploy_config_file(const char* file_name,
                                    const char* version_key) {
  (void)file_name;
  (void)version_key;
  shim_log("deploy_config_file");
  return shim_is_scenario("deploy_fail") ? False : True;
}

static RimeSessionId shim_create_session(void) {
  LONG call_number = InterlockedIncrement(&g_create_session_calls);
  RimeSessionId session_id;
  shim_enter_call("create_session_enter");
  if (shim_is_scenario("new_session_cancel") && call_number == 1) {
    Sleep(SHIM_DELAY_MS);
  }

  session_id = (RimeSessionId)InterlockedIncrement(&g_next_session_id);
  if (session_id < SHIM_SESSION_CAPACITY) {
    EnterCriticalSection(&g_state_lock);
    g_sessions[session_id] = 1;
    LeaveCriticalSection(&g_state_lock);
  }
  shim_leave_call("create_session_exit");
  return session_id;
}

static Bool shim_find_session(RimeSessionId session_id) {
  LONG entry_number = InterlockedIncrement(&g_find_session_entries);
  Bool result;
  if (entry_number >= 2 && g_second_find_session_entered) {
    SetEvent(g_second_find_session_entered);
  }
  shim_enter_call("find_session_enter");
  if (shim_is_scenario("gate") && g_find_session_release) {
    WaitForSingleObject(g_find_session_release, INFINITE);
  }
  result = shim_session_is_live(session_id);
  shim_leave_call("find_session_exit");
  return result;
}

static Bool shim_destroy_session(RimeSessionId session_id) {
  LONG call_number = InterlockedIncrement(&g_destroy_session_calls);
  Bool result = False;
  shim_log("destroy_session");
  if (shim_is_scenario("new_session_cancel") && call_number == 1) {
    shim_log("destroy_session_false");
    return False;
  }

  if (session_id < SHIM_SESSION_CAPACITY) {
    EnterCriticalSection(&g_state_lock);
    if (g_sessions[session_id]) {
      g_sessions[session_id] = 0;
      result = True;
    }
    LeaveCriticalSection(&g_state_lock);
  }
  return result;
}

static Bool shim_process_key(RimeSessionId session_id, int keycode, int mask) {
  (void)keycode;
  (void)mask;
  shim_log("process_key");
  return shim_session_is_live(session_id);
}

static Bool shim_get_commit(RimeSessionId session_id, RimeCommit* commit) {
  (void)session_id;
  (void)commit;
  return False;
}

static Bool shim_free_commit(RimeCommit* commit) {
  (void)commit;
  return True;
}

static Bool shim_get_context(RimeSessionId session_id, RimeContext* context) {
  (void)context;
  return shim_session_is_live(session_id);
}

static Bool shim_free_context(RimeContext* context) {
  (void)context;
  return True;
}

static Bool shim_get_status(RimeSessionId session_id, RimeStatus* status) {
  (void)status;
  return shim_session_is_live(session_id);
}

static Bool shim_free_status(RimeStatus* status) {
  (void)status;
  return True;
}

static void shim_set_option(RimeSessionId session_id,
                            const char* option,
                            Bool value) {
  (void)session_id;
  (void)option;
  (void)value;
}

static Bool shim_get_option(RimeSessionId session_id, const char* option) {
  (void)session_id;
  (void)option;
  return False;
}

static Bool shim_simulate_key_sequence(RimeSessionId session_id,
                                       const char* key_sequence) {
  (void)key_sequence;
  return shim_session_is_live(session_id);
}

static const char* shim_get_input(RimeSessionId session_id) {
  return shim_session_is_live(session_id) ? "" : NULL;
}

SHIM_EXPORT RimeApi* rime_get_api(void) {
  shim_ensure_initialized();
  return &g_api;
}

SHIM_EXPORT void rimesharp_shim_reset(void) {
  shim_ensure_initialized();
  EnterCriticalSection(&g_state_lock);
  memset(g_sessions, 0, sizeof(g_sessions));
  g_notification_handler = NULL;
  g_notification_context = NULL;
  LeaveCriticalSection(&g_state_lock);
  InterlockedExchange(&g_next_session_id, 0);
  InterlockedExchange(&g_initialize_calls, 0);
  InterlockedExchange(&g_create_session_calls, 0);
  InterlockedExchange(&g_destroy_session_calls, 0);
  InterlockedExchange(&g_active_calls, 0);
  InterlockedExchange(&g_max_concurrency, 0);
  InterlockedExchange(&g_find_session_entries, 0);
  if (g_find_session_release) {
    ResetEvent(g_find_session_release);
  }
  if (g_second_find_session_entered) {
    ResetEvent(g_second_find_session_entered);
  }
}

SHIM_EXPORT void rimesharp_shim_configure(const char* scenario,
                                          const wchar_t* log_path) {
  shim_ensure_initialized();
  EnterCriticalSection(&g_state_lock);
  strncpy(g_scenario, scenario ? scenario : "", SHIM_SCENARIO_CAPACITY - 1);
  g_scenario[SHIM_SCENARIO_CAPACITY - 1] = '\0';
  wcsncpy(g_log_path, log_path ? log_path : L"", SHIM_LOG_PATH_CAPACITY - 1);
  g_log_path[SHIM_LOG_PATH_CAPACITY - 1] = L'\0';
  LeaveCriticalSection(&g_state_lock);
}

SHIM_EXPORT void rimesharp_shim_reset_concurrency(void) {
  shim_ensure_initialized();
  InterlockedExchange(&g_active_calls, 0);
  InterlockedExchange(&g_max_concurrency, 0);
  InterlockedExchange(&g_find_session_entries, 0);
  if (g_find_session_release) {
    ResetEvent(g_find_session_release);
  }
  if (g_second_find_session_entered) {
    ResetEvent(g_second_find_session_entered);
  }
}

SHIM_EXPORT int rimesharp_shim_get_max_concurrency(void) {
  shim_ensure_initialized();
  return (int)InterlockedCompareExchange(&g_max_concurrency, 0, 0);
}

SHIM_EXPORT int rimesharp_shim_wait_for_second_find_session(
    int timeout_milliseconds) {
  DWORD timeout = timeout_milliseconds < 0
      ? INFINITE
      : (DWORD)timeout_milliseconds;
  shim_ensure_initialized();
  return g_second_find_session_entered &&
      WaitForSingleObject(g_second_find_session_entered, timeout) ==
          WAIT_OBJECT_0;
}

SHIM_EXPORT void rimesharp_shim_release_find_session(void) {
  shim_ensure_initialized();
  if (g_find_session_release) {
    SetEvent(g_find_session_release);
  }
}
