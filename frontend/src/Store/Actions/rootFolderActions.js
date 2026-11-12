import { createAction } from 'redux-actions';
import { batchActions } from 'redux-batched-actions';
import { createThunk, handleThunks } from 'Store/thunks';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import { set, updateItem } from './baseActions';
import createFetchHandler from './Creators/createFetchHandler';
import createHandleActions from './Creators/createHandleActions';
import createRemoveItemHandler from './Creators/createRemoveItemHandler';

//
// Variables

export const section = 'rootFolders';

//
// State

export const defaultState = {
  isFetching: false,
  isPopulated: false,
  error: null,
  isSaving: false,
  saveError: null,
  pendingChanges: {},
  items: []
};

//
// Actions Types

export const FETCH_ROOT_FOLDERS = 'rootFolders/fetchRootFolders';
export const ADD_ROOT_FOLDER = 'rootFolders/addRootFolder';
export const DELETE_ROOT_FOLDER = 'rootFolders/deleteRootFolder';
export const SET_ROOT_FOLDER_VALUE = 'rootFolders/setRootFolderValue';
export const SAVE_ROOT_FOLDERS = 'rootFolders/saveRootFolders';
export const CLEAR_ROOT_FOLDER_PENDING = 'rootFolders/clearRootFolderPending';

//
// Action Creators

export const fetchRootFolders = createThunk(FETCH_ROOT_FOLDERS);
export const addRootFolder = createThunk(ADD_ROOT_FOLDER);
export const deleteRootFolder = createThunk(DELETE_ROOT_FOLDER);
export const setRootFolderValue = createAction(SET_ROOT_FOLDER_VALUE);
export const saveRootFolders = createThunk(SAVE_ROOT_FOLDERS);
export const clearRootFolderPending = createAction(CLEAR_ROOT_FOLDER_PENDING);

//
// Action Handlers

export const actionHandlers = handleThunks({

  [FETCH_ROOT_FOLDERS]: createFetchHandler('rootFolders', '/rootFolder'),

  [DELETE_ROOT_FOLDER]: createRemoveItemHandler(
    'rootFolders',
    '/rootFolder',
    (state) => state.rootFolders
  ),

  [ADD_ROOT_FOLDER]: function(getState, payload, dispatch) {
    const path = payload.path;
    const folderLayout = payload.folderLayout || 'movieFolder';

    dispatch(set({
      section,
      isSaving: true
    }));

    const promise = createAjaxRequest({
      url: '/rootFolder',
      method: 'POST',
      data: JSON.stringify({ path, folderLayout }),
      dataType: 'json'
    }).request;

    promise.done((data) => {
      dispatch(batchActions([
        updateItem({
          section,
          ...data
        }),

        set({
          section,
          isSaving: false,
          saveError: null
        })
      ]));
    });

    promise.fail((xhr) => {
      dispatch(set({
        section,
        isSaving: false,
        saveError: xhr
      }));
    });
  },

  [SAVE_ROOT_FOLDERS]: function(getState, payload, dispatch) {
    const { items, pendingChanges } = getState().rootFolders;
    const ids = Object.keys(pendingChanges || {});

    if (!ids.length) {
      return;
    }

    dispatch(set({
      section,
      isSaving: true
    }));

    const saveNext = (index) => {
      if (index >= ids.length) {
        dispatch(set({
          section,
          isSaving: false,
          saveError: null,
          pendingChanges: {}
        }));
        dispatch(fetchRootFolders());
        return;
      }

      const id = Number(ids[index]);
      const item = items.find((folder) => folder.id === id);

      const promise = createAjaxRequest({
        url: `/rootFolder/${id}`,
        method: 'PUT',
        data: JSON.stringify({
          id,
          path: item.path,
          folderLayout: pendingChanges[ids[index]]
        }),
        dataType: 'json'
      }).request;

      promise.done((data) => {
        dispatch(updateItem({
          section,
          ...data
        }));
        saveNext(index + 1);
      });

      promise.fail((xhr) => {
        dispatch(set({
          section,
          isSaving: false,
          saveError: xhr
        }));
      });
    };

    saveNext(0);
  }

});

//
// Reducers

export const reducers = createHandleActions({

  [SET_ROOT_FOLDER_VALUE]: function(state, { payload }) {
    const id = String(payload.id);
    const pendingChanges = { ...state.pendingChanges };
    const item = state.items.find((folder) => folder.id === payload.id);
    const savedLayout = item && item.folderLayout ? item.folderLayout : 'movieFolder';

    if (payload.folderLayout === savedLayout) {
      delete pendingChanges[id];
    } else {
      pendingChanges[id] = payload.folderLayout;
    }

    return Object.assign({}, state, { pendingChanges });
  },

  [CLEAR_ROOT_FOLDER_PENDING]: function(state) {
    return Object.assign({}, state, { pendingChanges: {} });
  }

}, defaultState, section);
