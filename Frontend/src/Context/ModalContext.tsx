import React, {createContext, useContext, useRef, useState, ReactNode} from 'react';

interface ModalContextType {
  openModal: (content: ReactNode) => void;
  closeModal: () => void;
}

const ModalContext = createContext<ModalContextType | undefined>(undefined);

export const ModalProvider: React.FC<{children: ReactNode}> = ({children}) => {
  const modalRef = useRef<HTMLDialogElement>(null);
  const [modalContent, setModalContent] = useState<ReactNode>(null);

  const openModal = (content: ReactNode) => {
    setModalContent(content);
    if (modalRef.current) {
      modalRef.current.showModal();
    }
  };

  const closeModal = () => {
    setModalContent(null);
    if (modalRef.current) {
      modalRef.current.close();
    }
  };

  return (
    <ModalContext.Provider value={{openModal, closeModal}}>
      {children}
      <dialog ref={modalRef} className="modal modal-bottom sm:modal-middle" onClick={(e) => {
        if (e.target === modalRef.current) {
          closeModal();
        }
      }}>
        <div className="modal-box max-h-[96vh]" onClick={e => e.stopPropagation()}>
          {modalContent}
        </div>
        <form method="dialog" className="modal-backdrop" onClick={closeModal}>
          <button>close</button>
        </form>
      </dialog>
    </ModalContext.Provider>
  );
};

export const useModal = (): ModalContextType => {
  const context = useContext(ModalContext);
  if (!context) {
    throw new Error('useModal must be used within a ModalProvider');
  }
  return context;
};
