import React, { useCallback } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import Modal from 'Components/Modal/Modal';
import {
  DISMISSED_UPDATE_VERSION_KEY,
  setAppValue,
} from 'Store/Actions/appActions';
import AppUpdatedModalContent from './AppUpdatedModalContent';
import AppState from './State/AppState';

interface AppUpdatedModalProps {
  isOpen: boolean;
  onModalClose: (...args: unknown[]) => unknown;
}

function rememberDismissedVersion(version: string) {
  try {
    window.localStorage.setItem(DISMISSED_UPDATE_VERSION_KEY, version);
  } catch {
    // A storage failure must not leave the modal stuck open.
  }
}

function AppUpdatedModal(props: AppUpdatedModalProps) {
  const { isOpen, onModalClose } = props;
  const dispatch = useDispatch();
  const version = useSelector((state: AppState) => state.app.version);

  const dismiss = useCallback(() => {
    rememberDismissedVersion(version);
    dispatch(setAppValue({ isUpdated: false }));
  }, [dispatch, version]);

  const handleReload = useCallback(() => {
    dismiss();
    location.reload();
  }, [dismiss]);

  const handleModalClose = useCallback(() => {
    dismiss();
    onModalClose();
  }, [dismiss, onModalClose]);

  return (
    <Modal
      isOpen={isOpen}
      closeOnBackgroundClick={false}
      onModalClose={handleModalClose}
    >
      <AppUpdatedModalContent onModalClose={handleReload} />
    </Modal>
  );
}

export default AppUpdatedModal;
