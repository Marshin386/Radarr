import AppSectionState, {
  AppSectionDeleteState,
  AppSectionSaveState,
} from 'App/State/AppSectionState';
import RootFolder from 'typings/RootFolder';

interface RootFolderAppState
  extends AppSectionState<RootFolder>,
    AppSectionDeleteState,
    AppSectionSaveState {
  pendingChanges: Record<string, RootFolder['folderLayout']>;
}

export default RootFolderAppState;
